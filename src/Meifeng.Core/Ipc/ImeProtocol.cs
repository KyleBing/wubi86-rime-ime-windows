using System.Text;

namespace Meifeng.Core.Ipc;

// TSF 文本服务与服务进程之间的消息格式：4 字节小端长度 + UTF-8 正文，正文为若干行 key=value
// 值里的 % 、换行和 = 以 %25 / %0A / %0D / %3D 转义。C++ 一侧（src/TextService/Protocol.h）与此保持一致
public static class ImeProtocol
{
    // 每个 Windows 用户一个管道，名称里带会话号，避免多用户远程桌面串号
    public static string PipeName(int sessionId, string userName) => $"MeifengWubi-{sessionId}-{Sanitize(userName)}";

    // 服务进程投给文本服务隐藏窗口的消息：有待取的上屏 / 编码更新
    public const int WM_MEIFENG_UPDATE = 0x8000 + 0x4D46; // WM_APP + 'MF'

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    public static string Escape(string v) =>
        v.Replace("%", "%25").Replace("\n", "%0A").Replace("\r", "%0D").Replace("=", "%3D");

    public static string Unescape(string v)
    {
        if (!v.Contains('%')) return v;
        var sb = new StringBuilder(v.Length);
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] == '%' && IsHex(v, i + 1))
            {
                sb.Append((char)Convert.ToInt32(v.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                sb.Append(v[i]);
            }
        }
        return sb.ToString();
    }

    private static bool IsHex(string v, int at) =>
        at + 1 < v.Length && Uri.IsHexDigit(v[at]) && Uri.IsHexDigit(v[at + 1]);

    public static Dictionary<string, string> Decode(string body)
    {
        var map = new Dictionary<string, string>();
        foreach (var line in body.Split('\n'))
        {
            if (line.Length == 0) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq]] = Unescape(line[(eq + 1)..]);
        }
        return map;
    }

    public static string Encode(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in pairs) sb.Append(k).Append('=').Append(Escape(v)).Append('\n');
        return sb.ToString();
    }

    // 读一条完整消息；对端关闭时返回 null
    public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct)) return null;
        var length = BitConverter.ToInt32(header, 0);
        if (length < 0 || length > 4 * 1024 * 1024) return null;
        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct)) return null;
        return Encoding.UTF8.GetString(body);
    }

    public static async Task WriteMessageAsync(Stream stream, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var buffer = new byte[bytes.Length + 4];
        BitConverter.GetBytes(bytes.Length).CopyTo(buffer, 0);
        bytes.CopyTo(buffer, 4);
        await stream.WriteAsync(buffer, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
