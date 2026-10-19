using System.Globalization;
using Meifeng.Core.Lexicon;

namespace Meifeng.Core.Sync;

// 对比本地与线上用户词库。同一条看分组 + 词 + 编码，权重和备注用来判断是否一致
public static class DictSyncDiff
{
    public sealed record WordItem(string Key, string Word, string Code, string Priority, string Note, string GroupName)
    {
        // 词和编码，用来在差异列表里认这一条
        public string Brief => $"{Word}  {Code}";

        // 权重、备注、分组里有内容的才写出来
        public string Detail
        {
            get
            {
                var parts = new List<string>();
                if (Priority.Length > 0) parts.Add($"权重 {Priority}");
                if (Note.Length > 0) parts.Add($"备注 {Note}");
                if (GroupName.Length > 0) parts.Add(GroupName);
                return parts.Count == 0 ? "无权重、备注" : string.Join("  ", parts);
            }
        }
    }

    public sealed record ChangedPair(WordItem Local, WordItem Remote);

    public sealed record Result(
        int LocalCount,
        int RemoteCount,
        List<WordItem> OnlyLocal,
        List<WordItem> OnlyRemote,
        List<ChangedPair> Changed,
        int SameCount,
        bool HasRemote)
    {
        public bool IsIdentical => HasRemote && OnlyLocal.Count == 0 && OnlyRemote.Count == 0 && Changed.Count == 0;

        public string Summary
        {
            get
            {
                if (!HasRemote) return $"云端尚无该文件备份。本地 {LocalCount} 条，可上传覆盖。";
                if (IsIdentical) return $"本地与云端完全一致（{LocalCount} 条）。";
                return $"本地 {LocalCount} 条，云端 {RemoteCount} 条。仅本地 {OnlyLocal.Count}，仅云端 {OnlyRemote.Count}，内容不同 {Changed.Count}。";
            }
        }

        // 弹窗列表只写逐条差异。摘要已经显示在标题下，这里不再重复
        public string DetailReport(int maxPerSection = 800)
        {
            if (!HasRemote || IsIdentical) return "";
            var lines = new List<string>();
            AppendSection($"仅本地（{OnlyLocal.Count}）", OnlyLocal, lines, maxPerSection);
            AppendSection($"仅云端（{OnlyRemote.Count}）", OnlyRemote, lines, maxPerSection);
            if (Changed.Count > 0)
            {
                lines.Add($"内容不同（{Changed.Count}）");
                foreach (var pair in Changed.Take(maxPerSection))
                {
                    lines.Add("  " + pair.Local.Brief);
                    lines.Add("    本地  " + pair.Local.Detail);
                    lines.Add("    云端  " + pair.Remote.Detail);
                }
                var rest = Changed.Count - Math.Min(Changed.Count, maxPerSection);
                if (rest > 0) lines.Add($"  …还有 {rest} 条");
                lines.Add("");
            }
            return string.Join("\n", lines).Trim('\n');
        }

        private static void AppendSection(string title, List<WordItem> items, List<string> lines, int limit)
        {
            if (items.Count == 0) return;
            lines.Add(title);
            foreach (var item in items.Take(limit)) lines.Add("  " + item.Brief + "  " + item.Detail);
            var rest = items.Count - Math.Min(items.Count, limit);
            if (rest > 0) lines.Add($"  …还有 {rest} 条");
            lines.Add("");
        }
    }

    public static List<WordItem> Flatten(UserDictDocument document)
    {
        var list = new List<WordItem>();
        foreach (var group in document.Groups)
        {
            foreach (var e in group.Entries)
            {
                list.Add(new WordItem(IdentityKey(group.Name, e.Text, e.Code), e.Text, e.Code,
                    e.Weight?.ToString(CultureInfo.InvariantCulture) ?? "", e.Note, group.Name));
            }
        }
        return list;
    }

    public static Result Compare(UserDictDocument local, UserDictDocument? remote)
    {
        var localWords = Flatten(local);
        var remoteWords = remote != null ? Flatten(remote) : [];
        var localMap = new Dictionary<string, WordItem>();
        foreach (var i in localWords) localMap[i.Key] = i;
        var remoteMap = new Dictionary<string, WordItem>();
        foreach (var i in remoteWords) remoteMap[i.Key] = i;
        var onlyLocal = new List<WordItem>();
        var onlyRemote = new List<WordItem>();
        var changed = new List<ChangedPair>();
        var same = 0;
        foreach (var (key, l) in localMap)
        {
            if (!remoteMap.TryGetValue(key, out var r))
            {
                onlyLocal.Add(l);
                continue;
            }
            if (l.Priority == r.Priority && l.Note == r.Note) same++;
            else changed.Add(new ChangedPair(l, r));
        }
        foreach (var (key, r) in remoteMap)
        {
            if (!localMap.ContainsKey(key)) onlyRemote.Add(r);
        }
        onlyLocal.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        onlyRemote.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        changed.Sort((a, b) => string.CompareOrdinal(a.Local.Key, b.Local.Key));
        return new Result(localWords.Count, remoteWords.Count, onlyLocal, onlyRemote, changed, same, remote != null);
    }

    // 把云端没有的词条并入本地。编码和词相同就是同一条，权重不同也不另加一份
    public static (UserDictDocument Document, int Added) MergeRemoteIntoLocal(UserDictDocument local, UserDictDocument remote)
    {
        var result = local.Clone();
        var added = 0;
        if (result.IsGroupMode || remote.IsGroupMode || result.ShouldPreserveGroupHeaders)
        {
            var indexByName = new Dictionary<string, int>();
            for (var i = 0; i < result.Groups.Count; i++) indexByName[result.Groups[i].Name] = i;
            foreach (var rg in remote.Groups)
            {
                if (indexByName.TryGetValue(rg.Name, out var idx))
                {
                    var g = result.Groups[idx];
                    var keys = g.Entries.Select(e => IdentityKey(g.Name, e.Text, e.Code)).ToHashSet();
                    foreach (var e in rg.Entries)
                    {
                        if (!keys.Add(IdentityKey(rg.Name, e.Text, e.Code))) continue;
                        g.Entries.Add(e);
                        added++;
                    }
                }
                else
                {
                    result.Groups.Add(rg.Clone());
                    indexByName[rg.Name] = result.Groups.Count - 1;
                    added += rg.Entries.Count;
                }
            }
            if (!result.IsGroupMode && result.ShouldPreserveGroupHeaders) result.IsGroupMode = true;
        }
        else
        {
            var keys = result.AllEntries.Select(e => IdentityKey("", e.Text, e.Code)).ToHashSet();
            if (result.Groups.Count == 0) result.Groups.Add(new UserDictGroup(""));
            foreach (var e in remote.AllEntries)
            {
                if (!keys.Add(IdentityKey("", e.Text, e.Code))) continue;
                result.Groups[0].Entries.Add(e);
                added++;
            }
        }
        return (result, added);
    }

    // 对照上次同步快照做三方合并。NextBase 只推进双方已经一致的词条
    public sealed record ThreeWayMergeResult(
        UserDictDocument Merged,
        UserDictDocument NextBase,
        List<WordItem> Conflicts,
        int TookRemote,
        int Dropped,
        bool NeedsUpload,
        bool LocalChanged);

    private enum PickKind { Use, Drop, Conflict }

    public static ThreeWayMergeResult ThreeWayMerge(UserDictDocument? @base, UserDictDocument local, UserDictDocument remote)
    {
        var baseMap = @base != null ? IndexMap(@base) : new Dictionary<string, WordItem>();
        var localMap = IndexMap(local);
        var remoteMap = IndexMap(remote);
        var keys = baseMap.Keys.Union(localMap.Keys).Union(remoteMap.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var chosen = new Dictionary<string, WordItem>();
        var conflictKeys = new HashSet<string>();
        var conflicts = new List<WordItem>();
        int tookRemote = 0, dropped = 0;
        foreach (var key in keys)
        {
            baseMap.TryGetValue(key, out var b);
            localMap.TryGetValue(key, out var l);
            remoteMap.TryGetValue(key, out var r);
            var (kind, item) = Pick(b, l, r);
            switch (kind)
            {
                case PickKind.Use:
                    chosen[key] = item!;
                    if (r != null && SameContent(item!, r))
                    {
                        if (l != null)
                        {
                            if (!SameContent(l, r)) tookRemote++;
                        }
                        else
                        {
                            tookRemote++;
                        }
                    }
                    break;
                case PickKind.Drop:
                    if (l != null) dropped++;
                    break;
                case PickKind.Conflict:
                    conflicts.Add(item!);
                    conflictKeys.Add(key);
                    // 冲突时文件里先留本地版本，避免未上传就把云端改掉
                    if (l != null) chosen[key] = l;
                    break;
            }
        }
        var merged = DocumentFrom(chosen, local);
        var mergedMap = IndexMap(merged);
        var baseNext = new Dictionary<string, WordItem>();
        if (conflictKeys.Count == 0)
        {
            baseNext = mergedMap;
        }
        else
        {
            foreach (var (key, item) in remoteMap)
            {
                if (conflictKeys.Contains(key))
                {
                    if (baseMap.TryGetValue(key, out var old)) baseNext[key] = old;
                    continue;
                }
                if (chosen.TryGetValue(key, out var kept) && SameContent(kept, item)) baseNext[key] = item;
            }
            foreach (var key in conflictKeys)
            {
                if (baseMap.TryGetValue(key, out var old)) baseNext[key] = old;
            }
        }
        return new ThreeWayMergeResult(
            merged,
            DocumentFrom(baseNext, local),
            conflicts,
            tookRemote,
            dropped,
            conflictKeys.Count == 0 && !MapsEqual(mergedMap, remoteMap),
            !MapsEqual(mergedMap, localMap));
    }

    // 有快照时：只一边改过就采用那边；两边都改且内容不同则冲突。没有快照时，单边出现视为新增
    private static (PickKind, WordItem?) Pick(WordItem? b, WordItem? l, WordItem? r)
    {
        if (l != null && r != null && SameContent(l, r)) return (PickKind.Use, l);
        if (b == null && l != null && r != null) return (PickKind.Conflict, l);
        if (b != null && l != null && r != null)
        {
            var localChanged = !SameContent(l, b);
            var remoteChanged = !SameContent(r, b);
            if (localChanged && remoteChanged) return (PickKind.Conflict, l);
            if (remoteChanged) return (PickKind.Use, r);
            return (PickKind.Use, l);
        }
        if (b != null && l != null && r == null) return SameContent(l, b) ? (PickKind.Drop, null) : (PickKind.Conflict, l);
        if (b != null && l == null && r != null) return SameContent(r, b) ? (PickKind.Drop, null) : (PickKind.Conflict, r);
        if (l != null && r == null) return (PickKind.Use, l);
        if (l == null && r != null) return (PickKind.Use, r);
        return (PickKind.Drop, null);
    }

    // 同一条是分组里的编码 + 词。权重不参与认词
    private static string IdentityKey(string group, string text, string code) => $"{group}\t{text}\t{code}";

    private static Dictionary<string, WordItem> IndexMap(UserDictDocument d)
    {
        var map = new Dictionary<string, WordItem>();
        foreach (var i in Flatten(d)) map[i.Key] = i;
        return map;
    }

    private static bool SameContent(WordItem a, WordItem b) => a.Priority == b.Priority && a.Note == b.Note && a.GroupName == b.GroupName;

    private static bool MapsEqual(Dictionary<string, WordItem> a, Dictionary<string, WordItem> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var o) || !SameContent(v, o)) return false;
        }
        return true;
    }

    // 两份文档内容是否一致（自动同步判断本地有没有改过）
    public static bool SameDocument(UserDictDocument a, UserDictDocument b) => MapsEqual(IndexMap(a), IndexMap(b));

    // 按本地分组顺序放回词条；分组变了或是云端新词则追加到对应组
    private static UserDictDocument DocumentFrom(Dictionary<string, WordItem> chosen, UserDictDocument template)
    {
        var result = template.Clone();
        var used = new HashSet<string>();
        foreach (var g in result.Groups)
        {
            var kept = new List<UserDictEntry>();
            foreach (var e in g.Entries)
            {
                var key = IdentityKey(g.Name, e.Text, e.Code);
                if (!chosen.TryGetValue(key, out var item)) continue;
                kept.Add(e with { Weight = ParseInt(item.Priority), Note = item.Note });
                used.Add(key);
            }
            g.Entries = kept;
        }
        foreach (var key in chosen.Keys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            var item = chosen[key];
            var entry = new UserDictEntry(item.Code, item.Word, ParseInt(item.Priority), item.Note);
            var g = result.Groups.FirstOrDefault(x => x.Name == item.GroupName);
            if (g != null)
            {
                g.Entries.Add(entry);
            }
            else
            {
                result.Groups.Add(new UserDictGroup(item.GroupName, [entry]));
                if (item.GroupName.Length > 0) result.IsGroupMode = true;
            }
        }
        return result;
    }

    private static int? ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
