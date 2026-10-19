using System.Text.Json.Nodes;

namespace Meifeng.Core.Config;

public enum PunctuationMode { Direct, Candidates }

// 标点由输入法处理，不走 Rime 的标点表。默认全部直接上屏第一项
public static class PunctuationPrefs
{
    public sealed record Item(string Key, string[] Symbols, PunctuationMode DefaultMode = PunctuationMode.Direct);

    private const string StorageKey = "punct.outputMode";
    private const string SymbolsKey = "punct.symbols";

    // 符号表与 Resources/Rime/default.custom.yaml 的 half_shape 一致
    public static readonly IReadOnlyList<Item> Items =
    [
        new(",", ["，"]),
        new(".", ["。"]),
        new("<", ["《"]),
        new(">", ["》"]),
        new("/", ["/"]),
        new("?", ["？"]),
        new(";", ["；"]),
        new(":", ["："]),
        new("'", ["‘", "’"]),
        new("\"", ["“", "”"]),
        new("\\", ["、"]),
        new("|", ["|"]),
        new("`", ["`"]),
        new("~", ["~"]),
        new("!", ["！"]),
        new("@", ["@"]),
        new("#", ["#"]),
        new("%", ["%", "％", "°", "℃"]),
        new("$", ["￥"]),
        new("^", ["……"]),
        new("&", ["&"]),
        new("*", ["*"]),
        new("(", ["（"]),
        new(")", ["）"]),
        new("-", ["-"]),
        new("_", ["——"]),
        new("+", ["+"]),
        new("=", ["="]),
        new("[", ["【"]),
        new("]", ["】"]),
        new("{", ["「"]),
        new("}", ["」"])
    ];

    public static string Title(this PunctuationMode m) => m == PunctuationMode.Direct ? "直接上屏" : "候选列表";

    public static Item? ItemFor(string key) => Items.FirstOrDefault(i => i.Key == key);

    public static PunctuationMode ModeFor(string key)
    {
        if (PrefsStore.Shared.GetNode(StorageKey) is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var raw))
        {
            return raw == "candidates" ? PunctuationMode.Candidates : PunctuationMode.Direct;
        }
        return ItemFor(key)?.DefaultMode ?? PunctuationMode.Direct;
    }

    public static void SetMode(PunctuationMode mode, string key)
    {
        var o = PrefsStore.Shared.GetNode(StorageKey) as JsonObject ?? new JsonObject();
        o[key] = mode == PunctuationMode.Candidates ? "candidates" : "direct";
        PrefsStore.Shared.SetNode(StorageKey, o);
    }

    // 用户改过的输出；没改过则用默认符号表。空内容视为不保存，继续用默认
    public static string[] SymbolsFor(string key)
    {
        if (PrefsStore.Shared.GetNode(SymbolsKey) is JsonObject o && o[key] is JsonArray arr)
        {
            var list = arr.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Where(s => s.Length > 0).ToArray();
            if (list.Length > 0) return list;
        }
        return ItemFor(key)?.Symbols ?? [key];
    }

    public static void SetSymbols(IEnumerable<string> symbols, string key)
    {
        var o = PrefsStore.Shared.GetNode(SymbolsKey) as JsonObject ?? new JsonObject();
        var cleaned = symbols.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (cleaned.Count == 0) o.Remove(key);
        else o[key] = new JsonArray(cleaned.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        PrefsStore.Shared.SetNode(SymbolsKey, o);
    }

    // 丢掉已保存的输出和方式，下次读取回到默认：每键直接上屏第一项
    public static void RestoreDefaults()
    {
        PrefsStore.Shared.Remove(StorageKey);
        PrefsStore.Shared.Remove(SymbolsKey);
    }

    // 按空白拆成多项，一项里的符号可以是多个字，例如「……」
    public static string[] ParseSymbols(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
