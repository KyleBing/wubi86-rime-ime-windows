using System.Runtime.InteropServices;
using System.Text;
using Meifeng.Core.Config;
using Meifeng.Core.Lexicon;
using Meifeng.Core.Sync;
using static Meifeng.Core.Engine.RimeNative;

namespace Meifeng.Core.Engine;

// 一次读取的 Rime 状态。注意 get_commit 会清掉待上屏文字，所以同一轮只取一次
public sealed record RimeSnapshot(
    string? CommitText,
    string? Preedit,
    int CursorPos,
    List<RimeCandidateInfo> Candidates,
    int HighlightedIndex,
    int PageSize,
    int PageNo,
    bool IsLastPage,
    bool IsComposing,
    bool IsAsciiMode);

public sealed record RimeCandidateInfo(string Text, string Comment);

// 进程内唯一的 librime 会话。服务进程里所有输入框共用一个会话，调用方需持有 Lock
public sealed unsafe class RimeEngine
{
    public static RimeEngine Shared { get; } = new();

    // 所有 librime 调用都在这把锁里串行
    public object Lock { get; } = new();

    private RimeApi* _api;
    private nuint _session;
    private bool _apiStarted;

    public bool Started { get; private set; }

    // 引擎重启或重新部署后触发
    public event Action? Reloaded;

    private RimeEngine() { }

    private RimeApi* Api
    {
        get
        {
            if (_api == null) _api = rime_get_api();
            return _api;
        }
    }

    // 是否缺少编译产物，需要全量 deploy
    private static bool NeedsFullBuild =>
        !File.Exists(Path.Combine(RimeResourceDeployer.ResolveUserDataDir(), "build", "wubi86_jidian.table.bin"));

    // 确保会话可用；有部署标记且不在编码中时先增量部署
    public bool EnsureStarted(bool forceFullCheck = false)
    {
        lock (Lock)
        {
            var force = forceFullCheck || NeedsFullBuild;
            if (Started && _session != 0)
            {
                // 正在打字时先不部署，避免清掉当前编码。标记留到编码结束
                var composing = Snapshot(peekOnly: true)?.IsComposing == true;
                if (!composing && (UserDictWriter.ConsumeNeedsDeploy() || NeedsFullBuild))
                {
                    if (!Redeploy())
                    {
                        Started = false;
                        return StartLocked(force);
                    }
                    UserDictAutoSync.NoteEngineDidReload();
                    Reloaded?.Invoke();
                }
                return true;
            }
            return StartLocked(force);
        }
    }

    public void Shutdown()
    {
        lock (Lock)
        {
            StopLocked();
        }
    }

    // 造词后重载用户词。走增量部署，失败再整体重启
    public bool ReloadUserDictionary()
    {
        lock (Lock)
        {
            UserDictWriter.ConsumeNeedsDeploy();
            if (Started && _session != 0)
            {
                if (Redeploy())
                {
                    UserDictAutoSync.NoteEngineDidReload();
                    Reloaded?.Invoke();
                    return true;
                }
                NoteApiShutdown();
            }
            return StartLocked(false);
        }
    }

    // 没有未上屏编码时才加载；正在打字则留着标记，等编码结束再试
    public void FlushReloadIfIdle()
    {
        lock (Lock)
        {
            if (!Started) return;
            if (Snapshot(peekOnly: true)?.IsComposing == true) return;
            if (!UserDictWriter.PeekNeedsDeploy()) return;
            ReloadUserDictionary();
        }
    }

    // 全量部署会 finalize 全局 librime。先结束会话，执行完再重建
    public T PerformExternalDeploy<T>(Func<T> operation, out bool restarted)
    {
        lock (Lock)
        {
            StopLocked();
            var result = operation();
            UserDictWriter.ConsumeNeedsDeploy();
            NoteApiShutdown();
            restarted = StartLocked(false);
            return result;
        }
    }

    private bool StartLocked(bool forceFullCheck)
    {
        try
        {
            var dirs = RimeResourceDeployer.SyncBundledResourcesIfNeeded() ?? throw new InvalidOperationException("缺少内置 Rime 资源");
            // 共享目录单独指向内置预设，避免用户目录没有 default.yaml 时编译不出方案
            var shared = RimeResourceDeployer.ResolveSharedDataDir() ?? dirs.User;
            RimeResourceDeployer.EnsureMissingImportedDicts(dirs.User);
            StartBridge(shared, dirs.User, forceFullCheck);
            Started = true;
            UserDictAutoSync.NoteEngineDidReload();
            Log.Write($"rime started forceFull={forceFullCheck} user={dirs.User}");
            Reloaded?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"rime start failed: {e.Message}");
            Started = false;
            return false;
        }
    }

    // 对应 RimeBridge startWithSharedDir
    private void StartBridge(string sharedDir, string userDir, bool forceFullCheck)
    {
        var api = Api;
        if (api == null) throw new InvalidOperationException("rime_get_api failed");
        if (_apiStarted && _session != 0) return;
        Directory.CreateDirectory(userDir);
        var tableBin = Path.Combine(userDir, "build", "wubi86_jidian.table.bin");
        var hasTable = File.Exists(tableBin);
        // 已部署过则不全量重编；仅缺产物或显式要求时 full_check
        var doFull = forceFullCheck || !hasTable;
        var traits = MakeTraits(sharedDir, userDir, "rime.meifeng");
        api->setup(&traits);
        api->initialize(&traits);
        _apiStarted = true;
        var maintenance = api->start_maintenance(doFull ? True : False);
        if (maintenance != 0) api->join_maintenance_thread();
        // 轻量维护后仍无词库：再强制全量一次（首次安装）
        if (!File.Exists(tableBin) && !doFull)
        {
            if (api->start_maintenance(True) != 0) api->join_maintenance_thread();
        }
        _session = api->create_session();
        if (_session == 0)
        {
            api->finalize();
            _apiStarted = false;
            throw new InvalidOperationException("create_session failed");
        }
        SelectSchema("wubi86_jidian");
    }

    private void StopLocked()
    {
        var api = Api;
        if (api != null && _apiStarted)
        {
            if (_session != 0) api->destroy_session(_session);
            api->finalize();
        }
        _session = 0;
        _apiStarted = false;
        Started = false;
    }

    // 外部已经 finalize 全局 API，这里只丢掉失效会话
    private void NoteApiShutdown()
    {
        _session = 0;
        _apiStarted = false;
        Started = false;
    }

    // 增量部署后重建会话，不删 build
    private bool Redeploy()
    {
        var api = Api;
        if (api == null || !_apiStarted) return false;
        var ok = api->start_maintenance(True) != 0;
        if (ok) api->join_maintenance_thread();
        if (!ok) ok = api->deploy() != 0;
        if (_session != 0) api->destroy_session(_session);
        _session = api->create_session();
        if (_session != 0)
        {
            SelectSchema("wubi86_jidian");
        }
        else
        {
            // 会话没建起来时完整收尾；下次 start 才能从干净状态重新 initialize
            api->finalize();
            _apiStarted = false;
            Started = false;
        }
        return _session != 0;
    }

    private bool SelectSchema(string id)
    {
        if (_session == 0) return false;
        var bytes = Utf8Z(id);
        fixed (byte* p = bytes) return Api->select_schema(_session, p) != 0;
    }

    public bool ProcessKey(int keyCode, int modifiers)
    {
        if (!Started || _session == 0) return false;
        return Api->process_key(_session, keyCode, modifiers) != 0;
    }

    // 选当前页第 index 个（0 起）
    public bool SelectCandidateOnPage(int index)
    {
        if (!Started || _session == 0) return false;
        var api = Api;
        if (Has(api, (int)Marshal.OffsetOf<RimeApi>(nameof(RimeApi.select_candidate_on_current_page))) && api->select_candidate_on_current_page != null)
        {
            return api->select_candidate_on_current_page(_session, (nuint)index) != 0;
        }
        return api->process_key(_session, '1' + index, 0) != 0;
    }

    public bool ChangePage(bool backward)
    {
        if (!Started || _session == 0) return false;
        var api = Api;
        if (Has(api, (int)Marshal.OffsetOf<RimeApi>(nameof(RimeApi.change_page))) && api->change_page != null)
        {
            return api->change_page(_session, backward ? True : False) != 0;
        }
        return api->process_key(_session, backward ? 0xff55 : 0xff56, 0) != 0;
    }

    public void ClearComposition()
    {
        if (!Started || _session == 0) return;
        Api->clear_composition(_session);
    }

    // 原始按键序列。反查的 preedit 可能已经去掉前缀 z，不能用来判断是不是反查
    public string RawInput()
    {
        if (!Started || _session == 0 || Api->get_input == null) return "";
        return Utf8(Api->get_input(_session)) ?? "";
    }

    // 当前编码的全部候选（最多 120 条，排序窗口用）
    public List<string> AllCandidateTexts()
    {
        var list = new List<string>();
        if (!Started || _session == 0) return list;
        var api = Api;
        if (api->candidate_list_begin == null)
        {
            return Snapshot(peekOnly: true)?.Candidates.Select(c => c.Text).ToList() ?? list;
        }
        RimeCandidateListIterator it = default;
        if (api->candidate_list_begin(_session, &it) == 0)
        {
            return Snapshot(peekOnly: true)?.Candidates.Select(c => c.Text).ToList() ?? list;
        }
        while (api->candidate_list_next(&it) != 0)
        {
            list.Add(Utf8(it.candidate.text) ?? "");
            if (list.Count >= 120) break;
        }
        api->candidate_list_end(&it);
        return list;
    }

    // 读取当前状态；peekOnly 为 true 时不取待上屏文字（不清空 commit）
    public RimeSnapshot? Snapshot(bool peekOnly = false)
    {
        if (!Started || _session == 0) return null;
        var api = Api;
        string? commitText = null;
        if (!peekOnly)
        {
            RimeCommit commit = default;
            commit.data_size = sizeof(RimeCommit) - sizeof(int);
            if (api->get_commit(_session, &commit) != 0)
            {
                commitText = Utf8(commit.text);
                api->free_commit(&commit);
            }
        }
        string? preedit = null;
        var cursor = 0;
        var candidates = new List<RimeCandidateInfo>();
        int highlighted = 0, pageSize = 0, pageNo = 0;
        var isLastPage = false;
        RimeContext ctx = default;
        ctx.data_size = sizeof(RimeContext) - sizeof(int);
        if (api->get_context(_session, &ctx) != 0)
        {
            preedit = Utf8(ctx.composition.preedit);
            cursor = ctx.composition.cursor_pos;
            isLastPage = ctx.menu.is_last_page != 0;
            pageSize = ctx.menu.page_size;
            pageNo = ctx.menu.page_no;
            highlighted = ctx.menu.highlighted_candidate_index;
            for (var i = 0; i < ctx.menu.num_candidates; i++)
            {
                var c = ctx.menu.candidates[i];
                candidates.Add(new RimeCandidateInfo(Utf8(c.text) ?? "", Utf8(c.comment) ?? ""));
            }
            api->free_context(&ctx);
        }
        var composing = false;
        var ascii = false;
        RimeStatus status = default;
        status.data_size = sizeof(RimeStatus) - sizeof(int);
        if (api->get_status(_session, &status) != 0)
        {
            composing = status.is_composing != 0;
            ascii = status.is_ascii_mode != 0;
            api->free_status(&status);
        }
        return new RimeSnapshot(commitText, preedit, cursor, candidates, highlighted, pageSize, pageNo, isLastPage, composing, ascii);
    }

    // 清空 build 后用独立 traits 全量编译一遍，结束后 finalize（调用方须先停掉会话）
    public static void MaintenanceDeploy(string sharedDir, string userDir)
    {
        var api = rime_get_api();
        if (api == null) throw new InvalidOperationException("rime_get_api failed");
        Directory.CreateDirectory(userDir);
        // 清掉 build 缓存，强制按当前 yaml 全量重编
        var build = Path.Combine(userDir, "build");
        if (Directory.Exists(build))
        {
            try { Directory.Delete(build, true); } catch { }
        }
        var traits = MakeTraits(sharedDir, userDir, "rime.meifeng.deploy");
        api->setup(&traits);
        api->initialize(&traits);
        var ok = api->start_maintenance(True) != 0;
        if (ok) api->join_maintenance_thread();
        if (!ok) ok = api->deploy() != 0;
        api->finalize();
        if (!ok) throw new InvalidOperationException("start_maintenance failed");
    }

    // 进程内常驻的 UTF-8 字符串，librime 在 setup 时会复制
    private static readonly Dictionary<string, nint> PinnedStrings = new();

    private static nint Pin(string s)
    {
        lock (PinnedStrings)
        {
            if (PinnedStrings.TryGetValue(s, out var p)) return p;
            var bytes = Utf8Z(s);
            p = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            PinnedStrings[s] = p;
            return p;
        }
    }

    private static RimeTraits MakeTraits(string sharedDir, string userDir, string appName)
    {
        var t = new RimeTraits
        {
            data_size = sizeof(RimeTraits) - sizeof(int),
            shared_data_dir = Pin(sharedDir),
            user_data_dir = Pin(userDir),
            distribution_name = Pin("玫枫五笔"),
            distribution_code_name = Pin("MeifengWubi"),
            distribution_version = Pin("1.0.0"),
            app_name = Pin(appName),
            // modules 置空：仅内置 core，不加载 Lua 等外部插件
            modules = 0,
            min_log_level = 2,
            log_dir = Pin(LogDir)
        };
        return t;
    }

    // Windows 下没有 stderr 可看，日志写到容器目录的 logs
    private static string LogDir
    {
        get
        {
            var dir = Path.Combine(AppPaths.ContainerDir, "logs");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }

    private static byte[] Utf8Z(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        Array.Resize(ref bytes, bytes.Length + 1);
        return bytes;
    }
}
