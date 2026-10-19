namespace Meifeng.Core.Lexicon;

// 用户词条：码表列顺序 text / code / weight / note
public sealed record UserDictEntry(string Code, string Text, int? Weight = null, string Note = "")
{
    public string Id => $"{Code}|{Text}";
}

// 用户词库分组（码表助手的 ## 分组）
public sealed class UserDictGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; }
    public List<UserDictEntry> Entries { get; set; }

    public UserDictGroup(string name, IEnumerable<UserDictEntry>? entries = null, Guid? id = null)
    {
        Name = name;
        Entries = entries?.ToList() ?? [];
        if (id.HasValue) Id = id.Value;
    }

    // 展示名：空名显示为「- 未命名 -」
    public string DisplayName
    {
        get
        {
            var trimmed = Name.Trim();
            return trimmed.Length == 0 ? "- 未命名 -" : trimmed;
        }
    }

    public bool IsEmpty => Entries.Count == 0;

    // 深拷贝，避免多个文档共享同一列表
    public UserDictGroup Clone() => new(Name, Entries, Id);
}

// 带分组的完整用户词库
public sealed class UserDictDocument
{
    // 是否分组模式（dict_grouped）
    public bool IsGroupMode { get; set; }
    public List<UserDictGroup> Groups { get; set; }

    public UserDictDocument(bool isGroupMode, List<UserDictGroup> groups)
    {
        IsGroupMode = isGroupMode;
        Groups = groups;
    }

    public IEnumerable<UserDictEntry> AllEntries => Groups.SelectMany(g => g.Entries);

    public int EntryCount => Groups.Sum(g => g.Entries.Count);

    // 落盘时是否必须保留 ## 分组行
    public bool ShouldPreserveGroupHeaders =>
        IsGroupMode || Groups.Count > 1 || Groups.Any(g => g.Name.Trim().Length > 0);

    // 默认分组模式，便于管理
    public static UserDictDocument Empty => new(true, [new UserDictGroup("")]);

    public static UserDictDocument Flat(IEnumerable<UserDictEntry> entries) => new(false, [new UserDictGroup("", entries)]);

    public UserDictDocument Clone() => new(IsGroupMode, Groups.Select(g => g.Clone()).ToList());

    // 拖动排序：把 source 下标的分组移到 destination 之前
    public void MoveGroups(IEnumerable<int> source, int destination)
    {
        var sorted = source.Distinct().OrderBy(i => i).ToList();
        var moving = sorted.Select(i => Groups[i]).ToList();
        var removedBefore = sorted.Count(i => i < destination);
        foreach (var i in sorted.OrderByDescending(i => i)) Groups.RemoveAt(i);
        var dest = Math.Clamp(destination - removedBefore, 0, Groups.Count);
        Groups.InsertRange(dest, moving);
    }

    // 在指定下标前插入空分组
    public UserDictGroup InsertEmptyGroup(int index)
    {
        var group = new UserDictGroup("");
        Groups.Insert(Math.Clamp(index, 0, Groups.Count), group);
        return group;
    }

    public void DeleteGroup(Guid id) => Groups.RemoveAll(g => g.Id == id);

    // 删除词条但保留空分组
    public void RemoveEntries(Func<UserDictEntry, bool> predicate)
    {
        foreach (var g in Groups) g.Entries.RemoveAll(e => predicate(e));
    }

    // 将词条移到目标分组末尾
    public void MoveEntries(IReadOnlyList<UserDictEntry> entries, Guid targetId)
    {
        if (Groups.All(g => g.Id != targetId)) return;
        var ids = entries.Select(e => e.Id).ToHashSet();
        RemoveEntries(e => ids.Contains(e.Id));
        var dest = Groups.First(g => g.Id == targetId);
        foreach (var entry in entries)
        {
            dest.Entries.RemoveAll(e => e.Code == entry.Code && e.Text == entry.Text);
            dest.Entries.Add(entry);
        }
    }
}
