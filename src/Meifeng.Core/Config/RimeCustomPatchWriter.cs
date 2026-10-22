using System.Text;

namespace Meifeng.Core.Config;

// default.custom.yaml 的增量改写：只改菜单每页候选数和按键绑定，文件里其他 patch 原样保留。
// 之前只在带玫枫标记的文件上整体重写，内置模板没有标记，导致设置一直写不进去
public static class RimeCustomPatchWriter
{
    public const string BeginMarker = "# >>> 由玫枫五笔设置生成，改动设置会重写这一段";
    public const string EndMarker = "# <<< 由玫枫五笔设置生成";

    // 返回改写后的全文。text 为空（还没有 custom 文件）时按内置方案列表新建
    public static string Updated(string text, IReadOnlyList<string> schemaIds, int pageSize, IReadOnlyList<string> bindingLines)
    {
        var normalized = text.Replace("\r\n", "\n");
        var lines = (normalized.Length == 0 ? NewFileLines(schemaIds) : normalized.Split('\n').ToList());
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        // 没有顶层 patch 时补一个，设置才有地方写
        if (!lines.Any(l => IsKey(l, 0, "patch")))
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("patch:");
        }
        StripLegacyPathEntries(lines);
        SetPageSize(pageSize, lines);
        SetBindings(bindingLines, lines);
        return string.Join('\n', lines) + "\n";
    }

    // 新建文件：内置方案列表 + 空的 patch，候选数与按键随后补进去
    private static List<string> NewFileLines(IReadOnlyList<string> schemaIds)
    {
        var lines = new List<string>
        {
            "# 由玫枫五笔设置生成：改动「设置」里的候选数量与按键会更新下面的 patch",
            "patch:",
            "  schema_list:"
        };
        lines.AddRange(schemaIds.Select(id => "    - schema: " + id));
        return lines;
    }

    // 旧版写的是 "menu/page_size" 这类路径键，改成嵌套段之前先删掉，避免同键两处值
    private static void StripLegacyPathEntries(List<string> lines)
    {
        var patch = lines.FindIndex(l => IsKey(l, 0, "patch"));
        if (patch < 0) return;
        string[] legacy = ["menu/page_size", "key_binder/bindings"];
        var i = patch + 1;
        while (i < lines.Count)
        {
            var name = KeyName(lines[i]);
            if (name == null || Indent(lines[i]) != 2 || !legacy.Contains(name))
            {
                i++;
                continue;
            }
            lines.RemoveRange(i, BlockEnd(lines, i, 2) - i);
        }
    }

    // 把 menu.page_size 改成指定值；已有 menu 段就只改 page_size 一行
    private static void SetPageSize(int size, List<string> lines)
    {
        var patch = lines.FindIndex(l => IsKey(l, 0, "patch"));
        if (patch < 0) return;
        var end = PatchEnd(lines, patch);
        var pageLine = "    page_size: " + size;
        var menu = -1;
        for (var i = patch + 1; i < Math.Min(end, lines.Count); i++)
        {
            if (!IsKey(lines[i], 2, "menu")) continue;
            menu = i;
            break;
        }
        if (menu < 0)
        {
            lines.InsertRange(patch + 1, ["  menu:", pageLine]);
            return;
        }
        var menuEnd = BlockEnd(lines, menu, 2);
        for (var i = menu + 1; i < menuEnd; i++)
        {
            if (!IsKey(lines[i], 4, "page_size")) continue;
            lines[i] = pageLine;
            return;
        }
        lines.Insert(menu + 1, pageLine);
    }

    // 把按键绑定写进 key_binder.bindings 末尾的标记段，先清掉上一次写入的段和冲突的翻页 / 二三候选绑定
    private static void SetBindings(IReadOnlyList<string> bindings, List<string> lines)
    {
        StripManagedBlock(lines);
        var patch = lines.FindIndex(l => IsKey(l, 0, "patch"));
        if (patch < 0) return;
        var end = PatchEnd(lines, patch);
        var block = new List<string> { "      " + BeginMarker };
        block.AddRange(bindings.Select(b => "      " + b));
        block.Add("      " + EndMarker);

        var binder = -1;
        for (var i = patch + 1; i < Math.Min(end, lines.Count); i++)
        {
            if (!IsKey(lines[i], 2, "key_binder")) continue;
            binder = i;
            break;
        }
        if (binder < 0)
        {
            lines.InsertRange(patch + 1, ["  key_binder:", "    bindings:", .. block]);
            return;
        }
        var binderEnd = BlockEnd(lines, binder, 2);
        var bindingsLine = -1;
        for (var i = binder + 1; i < binderEnd; i++)
        {
            if (!IsKey(lines[i], 4, "bindings")) continue;
            bindingsLine = i;
            break;
        }
        if (bindingsLine < 0)
        {
            lines.InsertRange(binder + 1, ["    bindings:", .. block]);
            return;
        }
        var bindingsEnd = BlockEnd(lines, bindingsLine, 4);
        // 去掉已有的翻页 / 二三候选绑定，避免与设置冲突
        var kept = new List<string>();
        for (var i = bindingsLine + 1; i < bindingsEnd; i++)
        {
            if (!IsManagedBinding(lines[i])) kept.Add(lines[i]);
        }
        lines.RemoveRange(bindingsLine + 1, bindingsEnd - bindingsLine - 1);
        lines.InsertRange(bindingsLine + 1, kept.Concat(block));
    }

    // 删掉上一次写入的标记段；没有结束标记时删到当前列表结束
    private static void StripManagedBlock(List<string> lines)
    {
        var start = lines.FindIndex(l => Trimmed(l) == BeginMarker);
        if (start < 0) return;
        var stop = lines.Count;
        var end = -1;
        for (var i = start + 1; i < lines.Count; i++)
        {
            if (Trimmed(lines[i]) != EndMarker) continue;
            end = i;
            break;
        }
        if (end >= 0)
        {
            stop = end + 1;
        }
        else
        {
            var i = start + 1;
            while (i < lines.Count)
            {
                var line = lines[i];
                if (!IsBlank(line) && !IsComment(line) && Indent(line) <= 4) break;
                i++;
            }
            stop = i;
        }
        lines.RemoveRange(start, stop - start);
    }

    // 翻页与二三候选的绑定由设置生成，写之前先去掉，避免和设置冲突
    private static bool IsManagedBinding(string line)
    {
        var t = Trimmed(line);
        if (!t.StartsWith("- ", StringComparison.Ordinal) || !t.Contains("send:", StringComparison.Ordinal)) return false;
        string[] accepts =
        [
            "accept: minus", "accept: equal", "accept: comma", "accept: period",
            "accept: bracketleft", "accept: bracketright",
            "accept: semicolon, send: 2", "accept: apostrophe, send: 3"
        ];
        return accepts.Any(t.Contains);
    }

    // patch 顶层键之后、下一个顶层键之前的行
    private static int PatchEnd(List<string> lines, int start)
    {
        for (var i = start + 1; i < lines.Count; i++)
        {
            if (!IsBlank(lines[i]) && !IsComment(lines[i]) && Indent(lines[i]) == 0) return i;
        }
        return lines.Count;
    }

    // 某个键对应的子块结束位置：缩进回到 <= indentLimit 的第一行
    private static int BlockEnd(List<string> lines, int start, int indentLimit)
    {
        for (var i = start + 1; i < lines.Count; i++)
        {
            if (!IsBlank(lines[i]) && !IsComment(lines[i]) && Indent(lines[i]) <= indentLimit) return i;
        }
        return lines.Count;
    }

    private static bool IsKey(string line, int depth, string key) =>
        Indent(line) == depth && KeyName(line) == key;

    // 行首的键名，去掉引号；列表项、注释和空行都不算键
    private static string? KeyName(string line)
    {
        var t = Trimmed(line);
        if (t.Length == 0 || t.StartsWith('#') || t.StartsWith('-')) return null;
        var colon = t.IndexOf(':');
        if (colon < 0) return null;
        var name = t[..colon].Trim();
        return name.Trim('"', '\'');
    }

    private static int Indent(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    private static string Trimmed(string line) => line.Trim();

    private static bool IsBlank(string line) => Trimmed(line).Length == 0;

    private static bool IsComment(string line) => Trimmed(line).StartsWith('#');
}
