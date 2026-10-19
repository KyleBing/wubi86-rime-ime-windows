using System.Globalization;
using System.Text;
using Meifeng.Core.Config;
using Meifeng.Core.Lexicon;

namespace Meifeng.Core.Sync;

// 线上扩展词库：拉取后覆盖 wubi86_jidian_extra.dict.yaml。只写文件，不提供查看或编辑
public static class ExtraDictUpdater
{
    public static string ActivePath => Path.Combine(RimeResourceDeployer.ResolveUserDataDir(), WubiCloudConfig.ExtraDictFileName);

    public static int LocalEntryCount()
    {
        try { return UserDictWriter.ParseDocument(File.ReadAllText(ActivePath)).EntryCount; } catch { return 0; }
    }

    // 覆盖写入。Added 是相对拉取前本地没有的「词+编码」条数
    public static (int Added, int WordCount) ApplyOnlineItems(IReadOnlyList<WubiCloudApi.ExtraWordItem> items)
    {
        var previous = LocalKeys();
        var document = BuildDocument(items);
        var incoming = document.AllEntries.Select(e => $"{e.Text}\t{e.Code}").ToHashSet();
        var added = incoming.Count(k => !previous.Contains(k));
        File.WriteAllText(ActivePath, SerializeExtraYaml(document), new UTF8Encoding(false));
        WubiCloudConfig.LastExtraPullAt = DateTimeOffset.Now;
        UserDictWriter.MarkNeedsDeploy();
        return (added, document.EntryCount);
    }

    private static HashSet<string> LocalKeys()
    {
        try
        {
            return UserDictWriter.ParseDocument(File.ReadAllText(ActivePath)).AllEntries.Select(e => $"{e.Text}\t{e.Code}").ToHashSet();
        }
        catch
        {
            return [];
        }
    }

    // 按分类分组，组内按 id 排序
    public static UserDictDocument BuildDocument(IEnumerable<WubiCloudApi.ExtraWordItem> items)
    {
        var groups = new List<UserDictGroup>();
        string? lastName = null;
        foreach (var item in items.OrderBy(i => i.CategoryId).ThenBy(i => i.Id ?? 0))
        {
            // 编码按本地解析规则转成小写，否则每次都会算成新增
            var entry = new UserDictEntry(item.Code.ToLowerInvariant(), item.Word, Math.Max(0, item.Weight), item.Comment?.Trim() ?? "");
            if (lastName != item.CategoryName)
            {
                groups.Add(new UserDictGroup(item.CategoryName, [entry]));
                lastName = item.CategoryName;
            }
            else if (groups.Count > 0)
            {
                groups[^1].Entries.Add(entry);
            }
        }
        return new UserDictDocument(true, groups.Count == 0 ? [new UserDictGroup("")] : groups);
    }

    public static string SerializeExtraYaml(UserDictDocument document)
    {
        var version = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("# Rime dictionary\n# encoding: utf-8\n---\nname: wubi86_jidian_extra\nnote: \"扩展词库\"\n");
        sb.Append($"version: \"{version}\"\ndict_grouped: true\nsort: original\n...\n\n");
        var groups = document.Groups.Count == 0 ? [new UserDictGroup("")] : document.Groups;
        var body = string.Join("\n\n", groups.Select(g =>
        {
            var block = new StringBuilder($"## {g.Name}\n");
            foreach (var e in g.Entries) block.Append(Line(e)).Append('\n');
            var t = block.ToString();
            return t.EndsWith('\n') ? t[..^1] : t;
        }));
        if (body.Length > 0) sb.Append(body).Append('\n');
        return sb.ToString();
    }

    // 无权重且无备注时只写词和编码；有备注时补上权重列，避免备注错位
    private static string Line(UserDictEntry e)
    {
        var note = e.Note.Replace("\n", " ").Replace("\r", "").Trim();
        if (note.Length == 0)
        {
            return e.Weight is int w && w > 0 ? $"{e.Text}\t{e.Code}\t{w}" : $"{e.Text}\t{e.Code}";
        }
        return $"{e.Text}\t{e.Code}\t{e.Weight?.ToString(CultureInfo.InvariantCulture) ?? ""}\t{note}";
    }
}
