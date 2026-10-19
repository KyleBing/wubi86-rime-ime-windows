using System.Globalization;
using System.Text;
using Meifeng.Core.Config;
using Meifeng.Core.Sync;

namespace Meifeng.Core.Lexicon;

// 用户词库读写；列顺序与主码表一致：text / code / weight
// 分组机制对齐码表助手（wubi-dict-editor）：头部 dict_grouped: true，组用 ## 分组名，组间空一行
public static class UserDictWriter
{
    // 空行切组时无名组的落盘名
    public const string UnnamedFallback = "未命名";

    // 用户词库文件写入后触发（设置页刷新列表用）
    public static event Action? DidChange;

    public static UserDictDocument LoadDocument(string path)
    {
        try
        {
            return File.Exists(path) ? ParseDocument(File.ReadAllText(path, Encoding.UTF8)) : UserDictDocument.Empty;
        }
        catch
        {
            return UserDictDocument.Empty;
        }
    }

    // markDeploy 为 false 时只写快照，不通知引擎重载
    public static void SaveDocument(UserDictDocument document, string path, bool markDeploy = true)
    {
        var version = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        // 只要还有分组信息，落盘必须保留 ##，不可压成扁平表
        if (document.ShouldPreserveGroupHeaders)
        {
            sb.Append("# Rime dictionary\n# encoding: utf-8\n---\nname: wubi86_jidian_user\n");
            sb.Append($"version: \"{version}\"\ndict_grouped: true\nsort: by_weight\n...\n\n");
            var groups = document.Groups.Count == 0 ? [new UserDictGroup("")] : document.Groups;
            // 每组先写 ## 分组行，再写词条
            var body = string.Join("\n\n", groups.Select(group =>
            {
                var block = new StringBuilder($"## {group.Name}\n");
                foreach (var entry in group.Entries) block.Append(YamlLine(entry)).Append('\n');
                var text = block.ToString();
                return text.EndsWith('\n') ? text[..^1] : text;
            }));
            if (body.Length > 0) sb.Append(body).Append('\n');
        }
        else
        {
            sb.Append("# Rime dictionary\n# encoding: utf-8\n---\nname: wubi86_jidian_user\n");
            sb.Append($"version: \"{version}\"\ndict_grouped: false\nsort: by_weight\n...\n\n");
            foreach (var entry in document.AllEntries) sb.Append(YamlLine(entry)).Append('\n');
        }
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
        if (markDeploy) MarkNeedsDeploy();
    }

    // 解析 yaml：对齐码表助手 parseGroupMode / parseNormalMode
    public static UserDictDocument ParseDocument(string fileContent)
    {
        var normalized = fileContent.Replace("\r\n", "\n");
        var headerEnd = normalized.IndexOf("...", StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            if (normalized.Contains("##"))
            {
                var g = ParseGroupModeBody(normalized);
                return g.Count == 0 ? UserDictDocument.Empty : new UserDictDocument(true, g);
            }
            var flat = ParseFlatBody(normalized);
            return flat.Count == 0 ? UserDictDocument.Empty : UserDictDocument.Flat(flat);
        }
        var header = normalized[..(headerEnd + 3)];
        var body = normalized[(headerEnd + 3)..];
        var isGroupMode = header.Contains("dict_grouped: true") || body.Contains("##");
        if (isGroupMode)
        {
            var groups = ParseGroupModeBody(body);
            return new UserDictDocument(true, groups.Count == 0 ? [new UserDictGroup("")] : groups);
        }
        // 非分组：即使无词条也保持 flat，避免被当成分组模式
        return UserDictDocument.Flat(ParseFlatBody(body));
    }

    // ## 开组，空行切组并保留空组
    private static List<UserDictGroup> ParseGroupModeBody(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var groups = new List<UserDictGroup>();
        UserDictGroup? temp = null;
        var lastItemIsEmptyLine = false;
        foreach (var item in lines)
        {
            if (item.StartsWith("##", StringComparison.Ordinal))
            {
                // 仅当当前组已有名字时推进；无名但有词则补成未命名，避免丢词
                if (temp != null && temp.Name.Length > 0) groups.Add(temp);
                else if (temp != null && temp.Entries.Count > 0)
                {
                    temp.Name = UnnamedFallback;
                    groups.Add(temp);
                }
                temp = new UserDictGroup(item[2..].Trim(' ', '\t'));
                lastItemIsEmptyLine = false;
            }
            else if (item.Contains('\t'))
            {
                var entry = ParseEntryLine(item);
                if (entry == null) continue;
                temp ??= new UserDictGroup("");
                temp.Entries.Add(entry);
                lastItemIsEmptyLine = false;
            }
            else if (item.StartsWith('#'))
            {
                lastItemIsEmptyLine = false;
            }
            else
            {
                // 空行：切组；连续空行只切一次
                if (!lastItemIsEmptyLine && temp != null)
                {
                    if (temp.Name.Length == 0) temp.Name = UnnamedFallback;
                    groups.Add(temp);
                    temp = new UserDictGroup("");
                }
                lastItemIsEmptyLine = true;
            }
        }
        // 末尾组：有组名或有词都保留（空分组的 ## 行不可丢）
        if (temp != null && (temp.Name.Length > 0 || temp.Entries.Count > 0)) groups.Add(temp);
        return groups;
    }

    private static List<UserDictEntry> ParseFlatBody(string body)
    {
        var entries = new List<UserDictEntry>();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim(' ', '\t', '\r');
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var entry = ParseEntryLine(line);
            if (entry != null) entries.Add(entry);
        }
        return entries;
    }

    // 词库检索和删行也按同一规则拆列：优先按 tab，兼容空格分隔旧行
    public static UserDictEntry? ParseEntryLine(string line)
    {
        string[] parts = line.Contains('\t')
            ? line.Split('\t')
            : line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var text = parts[0];
        var code = parts[1].Replace("\r", "");
        // 第三列是权重；缺列或空列表示没有权重，不补默认值
        int? weight = null;
        if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
        {
            weight = w;
        }
        // 第四列及之后是备注
        var note = parts.Length >= 4 ? string.Join("\t", parts[3..]).Replace("\r", "").Trim() : "";
        var firstLooksLikeCode = text.EnumerateRunes().All(r => Rune.IsLetterOrDigit(r));
        var secondHasCJK = code.Any(c => c >= 0x4E00 && c <= 0x9FFF);
        if (firstLooksLikeCode && secondHasCJK) (text, code) = (code, text);
        return new UserDictEntry(code.ToLowerInvariant(), text, weight, note);
    }

    // 有备注时写出四列，避免备注被当成权重
    private static string YamlLine(UserDictEntry entry)
    {
        var note = entry.Note.Replace("\n", " ").Replace("\r", "").Trim();
        if (note.Length == 0)
        {
            return entry.Weight is int w ? $"{entry.Text}\t{entry.Code}\t{w}" : $"{entry.Text}\t{entry.Code}";
        }
        return $"{entry.Text}\t{entry.Code}\t{entry.Weight?.ToString(CultureInfo.InvariantCulture) ?? ""}\t{note}";
    }

    // ---- 部署标记：词库改动后由引擎在空闲时增量部署 ----

    private static string DeployFlagPath => Path.Combine(AppPaths.ContainerDir, ".needs_deploy");

    public static void MarkNeedsDeploy()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ContainerDir);
            File.WriteAllText(DeployFlagPath, "1");
        }
        catch
        {
            // 标记写不进去时下次启动仍会增量检查
        }
        NeedsDeployMarked?.Invoke();
    }

    // 标记部署后触发，引擎可立即尝试重载
    public static event Action? NeedsDeployMarked;

    public static bool ConsumeNeedsDeploy()
    {
        if (!PeekNeedsDeploy()) return false;
        try { File.Delete(DeployFlagPath); } catch { }
        return true;
    }

    // 只查看，不清标记。正在打字时先留着，等编码结束再部署
    public static bool PeekNeedsDeploy() => File.Exists(DeployFlagPath);

    // ---- 当前 Rime 目录下的用户词库 ----

    // 可写用户词库路径（与 Rime user_data_dir 一致）
    public static string ActivePath
    {
        get
        {
            var path = Path.Combine(RimeResourceDeployer.ResolveUserDataDir(), "wubi86_jidian_user.dict.yaml");
            if (!File.Exists(path))
            {
                try { SaveDocument(UserDictDocument.Empty, path); } catch { }
            }
            return path;
        }
    }

    public static UserDictDocument LoadActiveDocument() => LoadDocument(ActivePath);

    public static void SaveActiveDocument(UserDictDocument document)
    {
        SaveDocument(document, ActivePath);
        DidChange?.Invoke();
        // 自动同步自己写回时不要再排一次
        UserDictAutoSync.NoteLocalEdit();
    }

    // 写入首个分组；无分组时建未命名组
    public static void AddEntry(UserDictEntry entry)
    {
        var doc = LoadActiveDocument();
        if (doc.Groups.Count == 0) doc.Groups.Add(new UserDictGroup(""));
        AddEntry(entry, doc.Groups[0].Id, doc);
        SaveActiveDocument(doc);
    }

    // 按分组下标添加
    public static void AddEntry(UserDictEntry entry, int groupIndex)
    {
        var doc = LoadActiveDocument();
        if (groupIndex < 0 || groupIndex >= doc.Groups.Count) throw new InvalidOperationException("找不到目标分组");
        AddEntry(entry, doc.Groups[groupIndex].Id, doc);
        SaveActiveDocument(doc);
    }

    // 「全部」选中时添加：新建「- 未命名 -」组并插到最前
    public static Guid AddEntryInNewUnnamedGroup(UserDictEntry entry)
    {
        var doc = LoadActiveDocument();
        doc.RemoveEntries(e => e.Code == entry.Code && e.Text == entry.Text);
        var group = new UserDictGroup("- 未命名 -", [entry]);
        doc.Groups.Insert(0, group);
        SaveActiveDocument(doc);
        return group.Id;
    }

    // 组内按编码序插入；没写权重就保持为空
    public static void AddEntry(UserDictEntry entry, Guid groupId, UserDictDocument document)
    {
        var group = document.Groups.FirstOrDefault(g => g.Id == groupId)
                    ?? throw new InvalidOperationException("找不到目标分组");
        document.RemoveEntries(e => e.Code == entry.Code && e.Text == entry.Text);
        var list = group.Entries;
        var insertAt = list.Count;
        for (var i = 0; i < Math.Max(0, list.Count - 1); i++)
        {
            if (string.CompareOrdinal(entry.Code, list[i].Code) >= 0 && string.CompareOrdinal(entry.Code, list[i + 1].Code) <= 0)
            {
                insertAt = i + 1;
                break;
            }
        }
        if (list.Count == 0) insertAt = 0;
        else if (string.CompareOrdinal(entry.Code, list[0].Code) < 0) insertAt = 0;
        else if (string.CompareOrdinal(entry.Code, list[^1].Code) > 0) insertAt = list.Count;
        list.Insert(insertAt, entry);
    }

    public static void RemoveEntry(UserDictEntry entry)
    {
        var doc = LoadActiveDocument();
        doc.RemoveEntries(e => e.Code == entry.Code && e.Text == entry.Text);
        SaveActiveDocument(doc);
    }

    public static string ExportText()
    {
        try { return File.ReadAllText(ActivePath, Encoding.UTF8); } catch { return ""; }
    }

    public static void ImportText(string text)
    {
        var doc = ParseDocument(text);
        SaveActiveDocument(doc.Groups.Count == 0 ? UserDictDocument.Empty : doc);
    }
}
