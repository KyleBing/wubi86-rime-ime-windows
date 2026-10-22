using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Meifeng.Core.Input;
using Microsoft.Win32.SafeHandles;

namespace Meifeng.Core.Ipc;

// 服务进程的命名管道。每个 TSF 文本服务实例一条连接，请求 / 应答一一对应
[SupportedOSPlatform("windows")]
public sealed class PipeServer : IDisposable
{
    private readonly InputController _controller;
    private readonly IImeUi _ui;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    // 文本服务实例号 → 它的隐藏消息窗口，用于投递 WM_MEIFENG_UPDATE
    private readonly ConcurrentDictionary<long, nint> _clientWindows = new();

    // 语言栏菜单请求打开设置 / 部署 / 造词
    public event Action<string>? OpenRequested;

    public PipeServer(InputController controller, IImeUi ui)
    {
        _controller = controller;
        _ui = ui;
        _pipeName = ImeProtocol.PipeName(CurrentSessionId(), Environment.UserName);
        _controller.OutputPending += NotifyClient;
    }

    public string PipeName => _pipeName;

    // 开始监听；连接各自在后台任务里处理
    public void Start()
    {
        _ = Task.Run(AcceptLoop);
    }

    public void Dispose()
    {
        _controller.OutputPending -= NotifyClient;
        _cts.Cancel();
    }

    private async Task AcceptLoop()
    {
        var first = true;
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? stream = null;
            try
            {
                stream = CreateInstance(first);
                first = false;
                await stream.WaitForConnectionAsync(_cts.Token);
                var connected = stream;
                stream = null;
                _ = Task.Run(() => Serve(connected));
            }
            catch (OperationCanceledException)
            {
                stream?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                Log.Write($"pipe accept failed: {ex.Message}");
                await Task.Delay(500);
            }
        }
    }

    // 处理一条连接直到对端断开
    private async Task Serve(NamedPipeServerStream stream)
    {
        long client = 0;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var body = await ImeProtocol.ReadMessageAsync(stream, _cts.Token);
                if (body == null) break;
                var req = ImeProtocol.Decode(body);
                if (req.TryGetValue("client", out var c)) long.TryParse(c, out client);
                var resp = Handle(req);
                await ImeProtocol.WriteMessageAsync(stream, ImeProtocol.Encode(resp), _cts.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Log.Write($"pipe serve failed: {ex}");
        }
        finally
        {
            if (client != 0)
            {
                _clientWindows.TryRemove(client, out _);
                // 宿主进程退出或文本服务停用：相当于失焦
                if (_controller.ActiveClient == client) _controller.Blur(client);
            }
            stream.Dispose();
        }
    }

    // 处理一条请求，返回应答字段
    private List<KeyValuePair<string, string>> Handle(Dictionary<string, string> req)
    {
        var resp = new List<KeyValuePair<string, string>>();
        var cmd = req.GetValueOrDefault("cmd", "");
        var client = Long(req, "client");
        if (client != 0 && req.TryGetValue("hwnd", out var h) && long.TryParse(h, out var hwnd) && hwnd != 0)
        {
            _clientWindows[client] = (nint)hwnd;
        }
        if (req.ContainsKey("cw")) _ui.SetCaret(Int(req, "cx"), Int(req, "cy"), Int(req, "cw"), Int(req, "cht"));

        switch (cmd)
        {
            case "hello":
                resp.Add(new("pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)));
                break;
            case "key":
            {
                var key = new KeyEvent(Int(req, "vk"), req.GetValueOrDefault("ch", ""), (KeyMods)Int(req, "mods"), req.GetValueOrDefault("up") == "1");
                // 部署进行中拿不到锁：不吃键，避免卡住前台软件
                if (!Monitor.TryEnter(_controller.SyncRoot, 300))
                {
                    resp.Add(new("eaten", "0"));
                    break;
                }
                try
                {
                    var (eaten, output) = _controller.HandleKey(client, key);
                    resp.Add(new("eaten", eaten ? "1" : "0"));
                    AppendOutput(resp, output);
                }
                finally
                {
                    Monitor.Exit(_controller.SyncRoot);
                }
                break;
            }
            case "focus":
                AppendOutput(resp, req.GetValueOrDefault("on") == "1" ? _controller.Focus(client) : _controller.Blur(client));
                break;
            case "caret":
                // caps 由文本服务按当前键盘状态带上来，模式提示据此优先显示「大写」
                _controller.CaretReported(client, req.GetValueOrDefault("reason") == "focus", req.GetValueOrDefault("caps") == "1");
                break;
            case "fetch":
                if (_controller.FetchPending(client) is { } pending) AppendOutput(resp, pending);
                break;
            case "terminated":
                _controller.CompositionTerminated(client);
                break;
            case "toggle":
                _controller.ToggleAscii();
                break;
            case "open":
                var what = req.GetValueOrDefault("what", "");
                Task.Run(() => OpenRequested?.Invoke(what));
                break;
        }
        resp.Add(new("ascii", _controller.AsciiMode ? "1" : "0"));
        return resp;
    }

    // 把一次输出写进应答：commits 条数、每条上屏与替换长度、最终编码、是否要光标
    private static void AppendOutput(List<KeyValuePair<string, string>> resp, ImeOutput output)
    {
        resp.Add(new("commits", output.Commits.Count.ToString(CultureInfo.InvariantCulture)));
        for (var i = 0; i < output.Commits.Count; i++)
        {
            resp.Add(new($"commit{i}", output.Commits[i].Text));
            resp.Add(new($"replace{i}", output.Commits[i].Replace.ToString(CultureInfo.InvariantCulture)));
        }
        resp.Add(new("preedit", output.Preedit));
        if (output.WantCaret) resp.Add(new("caret", "1"));
    }

    // 有待取输出：投递消息让文本服务来 fetch
    private void NotifyClient(long client)
    {
        if (_clientWindows.TryGetValue(client, out var hwnd)) PostMessageW(hwnd, ImeProtocol.WM_MEIFENG_UPDATE, 0, 0);
    }

    private static int Int(Dictionary<string, string> req, string key) =>
        req.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static long Long(Dictionary<string, string> req, string key) =>
        req.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    // 新建一个管道实例。安全描述允许本用户、系统、管理员、所有应用容器，并带低完整性标签
    private NamedPipeServerStream CreateInstance(bool first)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "OW";
        var sddl = $"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{sid})(A;;GA;;;AC)(A;;GA;;;S-1-15-2-2)S:(ML;;NW;;;LW)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var sd, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd, bInheritHandle = 0 };
            var openMode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | (first ? FILE_FLAG_FIRST_PIPE_INSTANCE : 0);
            var handle = CreateNamedPipeW($@"\\.\pipe\{_pipeName}", openMode,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                PIPE_UNLIMITED_INSTANCES, 64 * 1024, 64 * 1024, 0, ref sa);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally
        {
            LocalFree(sd);
        }
    }

    // 当前进程所在的终端服务会话号
    private static int CurrentSessionId() =>
        ProcessIdToSessionId((uint)Environment.ProcessId, out var id) ? (int)id : 0;

    private const uint PIPE_ACCESS_DUPLEX = 0x3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
    private const uint PIPE_TYPE_BYTE = 0x0, PIPE_READMODE_BYTE = 0x0, PIPE_WAIT = 0x0, PIPE_REJECT_REMOTE_CLIENTS = 0x8;
    private const uint PIPE_UNLIMITED_INSTANCES = 255;

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances,
        uint outBuffer, uint inBuffer, uint defaultTimeout, ref SECURITY_ATTRIBUTES sa);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out nint sd, out uint size);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint mem);

    [DllImport("kernel32.dll")]
    private static extern bool ProcessIdToSessionId(uint pid, out uint sessionId);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hwnd, int msg, nint wParam, nint lParam);
}
