using System.Text;
using Meifeng.Core.Config;

namespace Meifeng.Core.Lexicon;

// 主词库自动编码（对齐 wubi-dict-editor DictMap）
public static class WubiDictEncoder
{
    private const string MainDictNameKey = "wubi.mainDict.fileName";
    public const string DefaultMainDictName = "wubi86_jidian.dict.yaml";
    public const string CharMapFileName = "wubi_charmap.tsv";

    private static readonly object Lock = new();
    private static Dictionary<string, string>? _cachedMap;

    public static string SelectedMainDictName
    {
        get => PrefsStore.Shared.GetString(MainDictNameKey) ?? DefaultMainDictName;
        set => PrefsStore.Shared.Set(MainDictNameKey, value);
    }

    public static string MainDictPath => Path.Combine(RimeResourceDeployer.ResolveUserDataDir(), SelectedMainDictName);

    public static string CharMapPath => Path.Combine(RimeResourceDeployer.ResolveUserDataDir(), CharMapFileName);

    // 用户目录下可选的码表文件
    public static List<string> AvailableDictFileNames()
    {
        try
        {
            return Directory.GetFiles(RimeResourceDeployer.ResolveUserDataDir(), "*.dict.yaml")
                .Select(Path.GetFileName)
                .Where(n => n != null && !n.Contains("user"))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static (bool Exists, int Count, DateTime? Modified) CharMapStatus()
    {
        var path = CharMapPath;
        if (!File.Exists(path)) return (false, 0, null);
        try
        {
            var count = File.ReadAllLines(path).Count(l => l.Contains('\t'));
            return (true, count, File.GetLastWriteTime(path));
        }
        catch
        {
            return (false, 0, null);
        }
    }

    // 从主词库提取单字码表并落盘
    public static int RebuildCharMap()
    {
        var path = MainDictPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"找不到主词库文件：{SelectedMainDictName}");
        var map = ExtractCharacterMap(File.ReadAllText(path, Encoding.UTF8));
        var sb = new StringBuilder();
        foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        File.WriteAllText(CharMapPath, sb.ToString(), new UTF8Encoding(false));
        lock (Lock) _cachedMap = map;
        return map.Count;
    }

    // 是否为汉字（CJK 统一汉字及扩展 A）
    public static bool IsChineseHanzi(Rune r)
    {
        var v = r.Value;
        return (v >= 0x4E00 && v <= 0x9FFF) || (v >= 0x3400 && v <= 0x4DBF);
    }

    // 按五笔规则给词条编码；仅匹配汉字，缺字时返回空串
    public static string Encode(string word)
    {
        var map = LoadCharMap();
        if (map.Count == 0) return "";
        // 带其它内容时只拿汉字参与编码
        var chars = word.EnumerateRunes().Where(IsChineseHanzi).Select(r => r.ToString()).ToList();
        if (chars.Count == 0) return "";
        if (chars.Count < 2) return map.TryGetValue(chars[0], out var single) ? single : "";
        if (chars.Count > 4) chars = [chars[0], chars[1], chars[2], chars[^1]];
        var codes = chars.Select(c => map.TryGetValue(c, out var code) ? code : "").ToList();
        if (codes.Any(c => c.Length == 0)) return "";
        static string P(string s, int n) => s.Length <= n ? s : s[..n];
        return codes.Count switch
        {
            2 => P(codes[0], 2) + P(codes[1], 2),
            3 => P(codes[0], 1) + P(codes[1], 1) + P(codes[2], 2),
            _ => P(codes[0], 1) + P(codes[1], 1) + P(codes[2], 1) + P(codes[^1], 1)
        };
    }

    private static Dictionary<string, string> LoadCharMap()
    {
        lock (Lock)
        {
            if (_cachedMap != null) return _cachedMap;
        }
        // 还没有单字字典时，用当前选中的主码表生成一次
        if (!File.Exists(CharMapPath))
        {
            try { RebuildCharMap(); } catch { }
            lock (Lock)
            {
                if (_cachedMap != null) return _cachedMap;
            }
        }
        var map = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadAllLines(CharMapPath, Encoding.UTF8))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length != 2 || DictLexiconIndex.TextLength(parts[0]) != 1) continue;
                map[parts[0]] = parts[1].ToLowerInvariant();
            }
        }
        catch
        {
            return map;
        }
        lock (Lock) _cachedMap = map;
        return map;
    }

    public static void ClearCache()
    {
        lock (Lock) _cachedMap = null;
    }

    // 提取单字：优先收录每个字第一条长度≥2 的编码
    public static Dictionary<string, string> ExtractCharacterMap(string fileContent)
    {
        var map = new Dictionary<string, string>();
        var inBody = false;
        foreach (var raw in fileContent.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = raw.Trim(' ', '\t');
            if (trimmed == "...")
            {
                inBody = true;
                continue;
            }
            if (!inBody && trimmed.StartsWith("---", StringComparison.Ordinal)) continue;
            // 无 yaml 头时整文件当码表体
            if (!inBody && raw.Contains('\t') && !trimmed.StartsWith('#') && !trimmed.StartsWith("name:", StringComparison.Ordinal))
            {
                inBody = true;
            }
            if (!inBody) continue;
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var parts = raw.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var text = parts[0];
            var code = parts[1].ToLowerInvariant();
            if (DictLexiconIndex.TextLength(text) != 1 || code.Length < 2) continue;
            map.TryAdd(text, code);
        }
        return map;
    }
}
