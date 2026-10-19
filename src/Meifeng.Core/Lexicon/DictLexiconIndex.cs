using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meifeng.Core.Config;

namespace Meifeng.Core.Lexicon;

// 一条码表命中：词条来自哪个文件、哪个分组
public sealed record DictLexiconHit(string FilePath, string GroupName, string SourceTitle, UserDictEntry Entry)
{
    public static readonly string[] ColumnTitles = ["词条", "编码", "权重", "词库", "分组"];

    // 同编码表的五列：词条、编码、权重、词库、分组。没有权重或分组就留空
    public string[] TableColumns =>
    [
        Entry.Text,
        Entry.Code,
        Entry.Weight?.ToString(CultureInfo.InvariantCulture) ?? "",
        LexiconName(Path.GetFileName(FilePath)),
        GroupName.Trim()
    ];

    // 表格排序：权重高在前，再按来源和词面；最多列 limit 条，返回剩余未列出的条数
    public static (List<DictLexiconHit> Shown, int Remaining) SortedForTable(IEnumerable<DictLexiconHit> hits, int limit = 40)
    {
        var sorted = hits.OrderByDescending(h => h.Entry.Weight ?? -1)
            .ThenBy(h => h.SourceTitle, StringComparer.Ordinal)
            .ThenBy(h => h.Entry.Text, StringComparer.Ordinal)
            .ToList();
        var shown = sorted.Take(limit).ToList();
        return (shown, sorted.Count - shown.Count);
    }

    public static string LexiconName(string fileName) => fileName switch
    {
        "wubi86_jidian.dict.yaml" => "主词库",
        "wubi86_jidian_user.dict.yaml" => "用户词",
        "wubi86_jidian_user_hamster.dict.yaml" => "Hamster 词库",
        "wubi86_jidian_extra.dict.yaml" => "扩展词库",
        _ => fileName.Replace(".dict.yaml", "")
    };
}

// 按编码 / 词面检索当前方案导入的码表，并支持从原文件删掉一行或改权重
public sealed class DictLexiconIndex
{
    public static DictLexiconIndex Shared { get; } = new();

    private Dictionary<string, List<DictLexiconHit>> _byCode = new();
    private Dictionary<string, List<DictLexiconHit>> _byText = new();
    private string _stamp = "";
    private readonly object _lock = new();

    // 造词或删词后丢弃缓存，下次查询按文件重新读
    public void Invalidate()
    {
        lock (_lock) InvalidateUnlocked();
    }

    public List<DictLexiconHit> Hits(string code)
    {
        lock (_lock)
        {
            ReloadIfNeeded();
            return _byCode.TryGetValue(code.ToLowerInvariant(), out var list) ? list.ToList() : [];
        }
    }

    public List<DictLexiconHit> HitsForText(string text)
    {
        lock (_lock)
        {
            ReloadIfNeeded();
            return _byText.TryGetValue(text, out var list) ? list.ToList() : [];
        }
    }

    // 候选条词后要标的编码。普通输入只标还没打的后几码；z 反查标词库里单字的五笔全码
    public List<string> DisplayCodes(IReadOnlyList<string> texts, string preedit, bool showFull)
    {
        lock (_lock)
        {
            ReloadIfNeeded();
            var reverse = IsReverseLookup(preedit);
            var prefix = new string(preedit.ToLowerInvariant().Where(c => c < 128 && char.IsLetter(c)).ToArray());
            return texts.Select(t => DisplayCodeUnlocked(t, prefix, reverse, showFull)).ToList();
        }
    }

    // 当前输入下，这个候选最可能对应的码，以及同词的其它编码
    public (List<DictLexiconHit> Hits, List<string> OtherCodes) Match(string text, string preedit)
    {
        lock (_lock)
        {
            ReloadIfNeeded();
            var prefix = new string(preedit.ToLowerInvariant().Where(char.IsLetter).ToArray());
            var all = (_byText.TryGetValue(text, out var l) ? l : [])
                .Where(h => prefix.Length == 0 || h.Entry.Code.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (all.Count == 0) return ([], []);
            string chosenCode;
            if (all.Any(h => h.Entry.Code == prefix))
            {
                chosenCode = prefix;
            }
            else
            {
                // 编码还没输完时，取最短的补全码（权重高的优先）
                chosenCode = all.OrderBy(h => h.Entry.Code.Length).ThenByDescending(h => h.Entry.Weight ?? 0).First().Entry.Code;
            }
            var chosen = all.Where(h => h.Entry.Code == chosenCode).ToList();
            var others = all.Select(h => h.Entry.Code).Distinct().Where(c => c != chosenCode).OrderBy(c => c, StringComparer.Ordinal).ToList();
            return (chosen, others);
        }
    }

    // 从命中的那个码表文件里删掉对应的一行
    public void Remove(DictLexiconHit hit)
    {
        lock (_lock)
        {
            RewriteMatchingLine(hit, _ => null, 1);
            UserDictWriter.MarkNeedsDeploy();
            InvalidateUnlocked();
        }
    }

    // 只改这一行的权重列，分组、备注和文件里的先后顺序保持不动
    public void UpdateWeight(DictLexiconHit hit, int weight)
    {
        lock (_lock)
        {
            RewriteMatchingLine(hit, line => LineReplacingWeight(line, weight), 2);
            UserDictWriter.MarkNeedsDeploy();
            InvalidateUnlocked();
        }
    }

    // 找到与命中相同的那一行；transform 返回 null 表示删除该行
    private static void RewriteMatchingLine(DictLexiconHit hit, Func<string, string?> transform, int code)
    {
        var raw = File.ReadAllText(hit.FilePath, Encoding.UTF8);
        var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n').ToList();
        var group = "";
        var done = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim(' ', '\t');
            if (trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                group = trimmed[2..].Trim(' ', '\t');
                continue;
            }
            if (done) continue;
            var entry = UserDictWriter.ParseEntryLine(line);
            if (entry == null || entry.Text != hit.Entry.Text || entry.Code != hit.Entry.Code || group != hit.GroupName) continue;
            var next = transform(line);
            if (next == null) lines.RemoveAt(i);
            else lines[i] = next;
            done = true;
            break;
        }
        if (!done) throw new InvalidOperationException($"词库里已经没有「{hit.Entry.Text}」。");
        var output = string.Join("\n", lines);
        if (normalized.EndsWith('\n') && !output.EndsWith('\n')) output += "\n";
        File.WriteAllText(hit.FilePath, output, new UTF8Encoding(false));
        _ = code;
    }

    // 第三列是权重。有备注或词根时留在后面，不整行重写
    private static string LineReplacingWeight(string line, int weight)
    {
        var value = weight.ToString(CultureInfo.InvariantCulture);
        if (line.Contains('\t'))
        {
            var parts = line.Split('\t').ToList();
            while (parts.Count < 3) parts.Add("");
            parts[2] = value;
            return string.Join("\t", parts);
        }
        var sp = line.Split(' ');
        if (sp.Length >= 3)
        {
            sp[2] = value;
            return string.Join(" ", sp);
        }
        return line + "\t" + value;
    }

    private static readonly Regex ReverseRegex = new(@"^z[a-z]*'?$");

    // z 开头、后面只有字母或结束符 '，与方案里的反查规则一致
    public static bool IsReverseLookup(string preedit)
    {
        var compact = new string(preedit.ToLowerInvariant().Where(c => c < 128 && (char.IsLetter(c) || c == '\'')).ToArray());
        return ReverseRegex.IsMatch(compact);
    }

    // 反查输入的是拼音，不能拿来对五笔码；词组没有对应五笔码时留空，不按拆字规则现造
    private string DisplayCodeUnlocked(string text, string prefix, bool reverse, bool showFull)
    {
        var all = _byText.TryGetValue(text, out var l) ? l : [];
        if (reverse)
        {
            if (TextLength(text) != 1) return "";
            return all.OrderByDescending(h => h.Entry.Code.Length).FirstOrDefault()?.Entry.Code ?? "";
        }
        var code = all.Where(h => prefix.Length == 0 || h.Entry.Code.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(h => h.Entry.Code.Length).ThenByDescending(h => h.Entry.Weight ?? 0)
            .FirstOrDefault()?.Entry.Code;
        if (code == null || !code.StartsWith(prefix, StringComparison.Ordinal)) return "";
        // 补码时已经打过的字母不再重复显示
        if (showFull || prefix.Length == 0) return code;
        return code[prefix.Length..];
    }

    // 按用户感知的字数计长度（扩展区汉字是代理对）
    public static int TextLength(string s) => new StringInfo(s).LengthInTextElements;

    private void InvalidateUnlocked()
    {
        _stamp = "";
        _byCode = new();
        _byText = new();
    }

    private void ReloadIfNeeded()
    {
        var files = LexiconFiles();
        var next = string.Join("\n", files.Select(f =>
        {
            var info = new FileInfo(f);
            return $"{f}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}|{(info.Exists ? info.Length : 0)}";
        }));
        if (next == _stamp) return;
        var codes = new Dictionary<string, List<DictLexiconHit>>();
        var texts = new Dictionary<string, List<DictLexiconHit>>();
        foreach (var f in files) Index(f, codes, texts);
        _byCode = codes;
        _byText = texts;
        _stamp = next;
    }

    // 主词库加上它 import_tables 里列出的码表
    private static List<string> LexiconFiles()
    {
        var dir = RimeResourceDeployer.ResolveUserDataDir();
        var main = Path.Combine(dir, WubiDictEncoder.SelectedMainDictName);
        var list = new List<string>();
        if (!File.Exists(main)) return list;
        list.Add(main);
        string text;
        try { text = File.ReadAllText(main, Encoding.UTF8); } catch { return list; }
        var inImport = false;
        foreach (var raw in text.Split('\n'))
        {
            var trimmed = raw.Trim(' ', '\t', '\r');
            if (trimmed == "...") break;
            if (trimmed.StartsWith("import_tables:", StringComparison.Ordinal))
            {
                inImport = true;
                continue;
            }
            if (!inImport) continue;
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            if (!trimmed.StartsWith('-')) break;
            var name = trimmed.TrimStart('-').Trim(' ', '\t');
            var hash = name.IndexOf('#');
            if (hash >= 0) name = name[..hash].Trim(' ', '\t');
            if (name.Length == 0) continue;
            var file = name.EndsWith(".dict.yaml", StringComparison.Ordinal) ? name : name + ".dict.yaml";
            var path = Path.Combine(dir, file);
            if (File.Exists(path) && !list.Contains(path)) list.Add(path);
        }
        return list;
    }

    private static void Index(string path, Dictionary<string, List<DictLexiconHit>> codes, Dictionary<string, List<DictLexiconHit>> texts)
    {
        string raw;
        try { raw = File.ReadAllText(path, Encoding.UTF8); } catch { return; }
        var fileName = Path.GetFileName(path);
        var normalized = raw.Replace("\r\n", "\n");
        var grouped = normalized.Contains("\n##") || normalized.StartsWith("##", StringComparison.Ordinal);
        var group = "";
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim(' ', '\t');
            if (trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                group = trimmed[2..].Trim(' ', '\t');
                continue;
            }
            var entry = UserDictWriter.ParseEntryLine(line);
            if (entry == null) continue;
            var hit = new DictLexiconHit(path, group, SourceTitle(fileName, group, grouped), entry);
            if (!codes.TryGetValue(entry.Code, out var cl)) codes[entry.Code] = cl = [];
            cl.Add(hit);
            if (!texts.TryGetValue(entry.Text, out var tl)) texts[entry.Text] = tl = [];
            tl.Add(hit);
        }
    }

    // 文件名换成用户能看懂的词库名；分组码表再带上组名
    private static string SourceTitle(string fileName, string groupName, bool grouped)
    {
        var b = DictLexiconHit.LexiconName(fileName);
        if (!grouped) return b;
        var name = groupName.Trim();
        return name.Length == 0 ? $"{b} · 未分组" : $"{b} · {name}";
    }
}
