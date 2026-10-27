using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Meifeng.Core.Config;

public enum CandidateOrientation { Horizontal, Vertical }

// 编码显示范围：补码只显示还没打的字母，完整显示词条的全部编码
public enum CodeDetail { Remainder, Full }

// 候选序号样式：不显示 / 带点（1.）/ 不带点（1）
public enum IndexStyle { Hidden, WithDot, Plain }

// 候选显示方式：候选框 / 仅数量圆点 / 不显示
public enum CandidateDisplayMode { Panel, Dots, Hidden }

public static class AppearanceTitles
{
    public static string Title(this CandidateOrientation o) => o == CandidateOrientation.Horizontal ? "水平" : "垂直";

    public static string Title(this CodeDetail d) => d == CodeDetail.Remainder ? "补码" : "完整";

    public static string Title(this IndexStyle s) => s switch { IndexStyle.Hidden => "无", IndexStyle.WithDot => "1.", _ => "1" };

    // 生成页内序号文案；Hidden 返回 null
    public static string? Label(this IndexStyle s, int oneBased) => s switch
    {
        IndexStyle.Hidden => null,
        IndexStyle.WithDot => $"{oneBased}.",
        _ => $"{oneBased}"
    };

    public static string Title(this CandidateDisplayMode m) => m switch
    {
        CandidateDisplayMode.Panel => "候选框",
        CandidateDisplayMode.Dots => "仅圆点",
        _ => "不显示"
    };
}

// 皮肤导入错误
public sealed class SkinImportException(string message) : Exception(message);

// 候选栏外观偏好：皮肤、排列、序号、编码显示等
public static class AppearancePrefs
{
    public const int SkinSlotCount = 8;

    private const string KeyOrientation = "candidate.orientation";
    private const string KeyShowPreedit = "candidate.showPreedit";
    private const string KeyShowComment = "candidate.showComment";
    private const string KeyShowCaretModeHint = "input.showCaretModeHint";
    private const string KeyCodeDetail = "candidate.codeDetail";
    private const string KeyIndexStyle = "candidate.indexStyle";
    private const string KeyShowCountDotsOnly = "candidate.showCountDotsOnly";
    private const string KeyHidePanel = "candidate.hidePanel";
    private const string KeySkin = "candidate.skin.v1";
    private const string KeySkinSlots = "candidate.skin.slots.v1";

    // 外观任一项变化后触发
    public static event Action? Changed;

    private static readonly object CacheLock = new();
    private static CandidateSkin? _cachedSkin;

    private static PrefsStore Store => PrefsStore.Shared;

    public static readonly JsonSerializerOptions SkinJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static CandidateSkin Skin
    {
        get
        {
            lock (CacheLock)
            {
                if (_cachedSkin != null) return _cachedSkin;
            }
            CandidateSkin value;
            var node = Store.GetNode(KeySkin);
            var decoded = node is JsonObject obj ? Decode(obj) : null;
            if (decoded == null || decoded.Version > CandidateSkin.CurrentVersion)
            {
                value = CandidateSkin.Default;
            }
            else
            {
                value = decoded.Normalized();
                var corrected = CorrectedCoralHighlightCode(value);
                if (corrected != value)
                {
                    // 已套用的珊瑚红仍带着旧的灰色首选编码，读到时改成白色
                    Store.SetNode(KeySkin, JsonSerializer.SerializeToNode(corrected, SkinJsonOptions));
                }
                value = corrected;
            }
            lock (CacheLock) _cachedSkin = value;
            return value;
        }
        set
        {
            var normalized = value.Normalized();
            lock (CacheLock) _cachedSkin = normalized;
            Store.SetNode(KeySkin, JsonSerializer.SerializeToNode(normalized, SkinJsonOptions));
            Notify();
        }
    }

    // 以稳定、易读的 JSON 格式导出（键排序），与 macOS 版互通
    public static string ExportedSkinJson()
    {
        var node = JsonSerializer.SerializeToNode(Skin, SkinJsonOptions)!;
        return SortKeys(node).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static void ImportSkinJson(string json)
    {
        JsonObject obj;
        try
        {
            obj = JsonNode.Parse(json) as JsonObject ?? throw new SkinImportException("无法读取皮肤配置：不是 JSON 对象");
        }
        catch (JsonException e)
        {
            throw new SkinImportException($"无法读取皮肤配置：{e.Message}");
        }
        CandidateSkin decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<CandidateSkin>(CompatibleSkinJson(obj), SkinJsonOptions)
                      ?? throw new SkinImportException("无法读取皮肤配置：内容为空");
        }
        catch (JsonException e)
        {
            throw new SkinImportException($"无法读取皮肤配置：{e.Message}");
        }
        if (decoded.Version < 1 || decoded.Version > CandidateSkin.CurrentVersion)
        {
            throw new SkinImportException($"不支持皮肤配置版本 {decoded.Version}。");
        }
        Skin = decoded;
    }

    public static void RestoreDefaultSkin() => Skin = CandidateSkin.Default;

    // 用户保存的主题，固定 8 个位置，null 表示空位
    public static CandidateSkin?[] SkinSlots
    {
        get
        {
            var result = new CandidateSkin?[SkinSlotCount];
            if (Store.GetNode(KeySkinSlots) is JsonArray arr)
            {
                for (var i = 0; i < SkinSlotCount && i < arr.Count; i++)
                {
                    if (arr[i] is JsonObject o) result[i] = Decode(o)?.Normalized();
                }
            }
            return result;
        }
        set
        {
            var arr = new JsonArray();
            for (var i = 0; i < SkinSlotCount; i++)
            {
                var v = i < value.Length ? value[i] : null;
                arr.Add(v == null ? null : JsonSerializer.SerializeToNode(v.Normalized(), SkinJsonOptions));
            }
            Store.SetNode(KeySkinSlots, arr);
            Notify();
        }
    }

    // 把当前皮肤存入指定位置（覆盖原有内容）
    public static void SaveCurrentSkin(int slot)
    {
        if (slot < 0 || slot >= SkinSlotCount) return;
        var slots = SkinSlots;
        slots[slot] = Skin;
        SkinSlots = slots;
    }

    public static void ClearSkinSlot(int slot)
    {
        if (slot < 0 || slot >= SkinSlotCount) return;
        var slots = SkinSlots;
        slots[slot] = null;
        SkinSlots = slots;
    }

    // 应用已保存的主题，完整还原（含字体）
    public static void ApplySkinSlot(int slot)
    {
        if (slot < 0 || slot >= SkinSlotCount) return;
        if (SkinSlots[slot] is { } saved) Skin = saved;
    }

    // 应用内置主题；内置主题不指定字体，沿用用户当前字体
    public static void ApplyPreset(CandidateSkin preset) => Skin = preset with { FontName = Skin.FontName };

    public static void UpdateSkin(Func<CandidateSkin, CandidateSkin> change) => Skin = change(Skin);

    public static CandidateOrientation Orientation
    {
        get => Store.GetString(KeyOrientation) == "vertical" ? CandidateOrientation.Vertical : CandidateOrientation.Horizontal;
        set
        {
            Store.Set(KeyOrientation, value == CandidateOrientation.Vertical ? "vertical" : "horizontal");
            Notify();
        }
    }

    public static CodeDetail CodeDetail
    {
        get => Store.GetString(KeyCodeDetail) == "full" ? CodeDetail.Full : CodeDetail.Remainder;
        set
        {
            Store.Set(KeyCodeDetail, value == CodeDetail.Full ? "full" : "remainder");
            Notify();
        }
    }

    // 输入光标出现时，是否在旁边显示当前是中文还是英文。默认开启
    public static bool ShowCaretModeHint
    {
        get => Store.GetBool(KeyShowCaretModeHint) ?? true;
        set
        {
            Store.Set(KeyShowCaretModeHint, value);
            Notify();
        }
    }

    // 是否在每个候选词后面显示它的五笔编码。默认不显示
    public static bool ShowPreeditInPanel
    {
        get => Store.GetBool(KeyShowPreedit) ?? false;
        set
        {
            Store.Set(KeyShowPreedit, value);
            Notify();
        }
    }

    // 是否在每个候选词后面显示 Rime 提供的备注（comment）。默认显示
    public static bool ShowCommentInPanel
    {
        get => Store.GetBool(KeyShowComment) ?? true;
        set
        {
            Store.Set(KeyShowComment, value);
            Notify();
        }
    }

    // 每页候选数（写入 Rime menu/page_size，见 BehaviorPrefs）
    public static int PageSize
    {
        get => BehaviorPrefs.PageSize;
        set => BehaviorPrefs.PageSize = value;
    }

    public static IndexStyle IndexStyle
    {
        get => Store.GetString(KeyIndexStyle) switch
        {
            "hidden" => IndexStyle.Hidden,
            "plain" => IndexStyle.Plain,
            _ => IndexStyle.WithDot
        };
        set
        {
            Store.Set(KeyIndexStyle, value switch { IndexStyle.Hidden => "hidden", IndexStyle.Plain => "plain", _ => "withDot" });
            Notify();
        }
    }

    public static CandidateDisplayMode DisplayMode
    {
        get
        {
            if (Store.GetBool(KeyHidePanel) == true) return CandidateDisplayMode.Hidden;
            return Store.GetBool(KeyShowCountDotsOnly) == true ? CandidateDisplayMode.Dots : CandidateDisplayMode.Panel;
        }
        set
        {
            Store.Set(KeyHidePanel, value == CandidateDisplayMode.Hidden);
            Store.Set(KeyShowCountDotsOnly, value == CandidateDisplayMode.Dots);
            Notify();
        }
    }

    private static void Notify() => Changed?.Invoke();

    private static CandidateSkin? Decode(JsonObject obj)
    {
        try
        {
            return JsonSerializer.Deserialize<CandidateSkin>(CompatibleSkinJson(obj), SkinJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // 珊瑚红首选底是红色，灰色编码看不清。只改还停在那组灰色上的首选编码
    private static CandidateSkin CorrectedCoralHighlightCode(CandidateSkin skin)
    {
        if (skin.Name != "珊瑚红") return skin;
        var grayLight = RGBAColor.Gray(0.3245130565068493);
        var grayDark = RGBAColor.Gray(0.7462810359589042);
        var white = new RGBAColor(1, 1, 1);
        var value = skin;
        if (Nearly(value.Light.HighlightCodeText, grayLight)) value = value with { Light = value.Light with { HighlightCodeText = white } };
        if (Nearly(value.Dark.HighlightCodeText, grayDark)) value = value with { Dark = value.Dark with { HighlightCodeText = white } };
        return value;
    }

    private static bool Nearly(RGBAColor a, RGBAColor b) =>
        Math.Abs(a.Red - b.Red) < 0.002 && Math.Abs(a.Green - b.Green) < 0.002 && Math.Abs(a.Blue - b.Blue) < 0.002;

    // 旧皮肤没有纵向对齐时按居中补齐；旧水平/垂直边距拆成四边；缺失的新字段取默认值
    private static JsonObject CompatibleSkinJson(JsonObject source)
    {
        var obj = (JsonObject)source.DeepClone();
        obj.TryAdd("verticalAlignment", "center");
        obj.TryAdd("offsetTop", 6);
        obj.TryAdd("offsetLeft", 0);
        if (!obj.ContainsKey("paddingTop") || !obj.ContainsKey("paddingBottom") || !obj.ContainsKey("paddingLeft") || !obj.ContainsKey("paddingRight"))
        {
            var x = obj["paddingX"]?.GetValue<double>() ?? 10;
            var y = obj["paddingY"]?.GetValue<double>() ?? 5;
            obj.TryAdd("paddingTop", y);
            obj.TryAdd("paddingBottom", y);
            obj.TryAdd("paddingLeft", x);
            obj.TryAdd("paddingRight", x);
        }
        // 旧的首选左右留白 highlightPaddingX 拆到左右两边
        if (obj["highlightPaddingX"] is { } hx)
        {
            obj.TryAdd("highlightPaddingLeft", hx.DeepClone());
            obj.TryAdd("highlightPaddingRight", hx.DeepClone());
        }
        // 其余新增字段（含亮/暗配色里的新颜色）按默认皮肤补齐
        if (JsonSerializer.SerializeToNode(CandidateSkin.Default, SkinJsonOptions) is JsonObject fallback)
        {
            foreach (var (key, value) in fallback)
            {
                if (value is JsonObject nested && obj[key] is JsonObject existing)
                {
                    foreach (var (sk, sv) in nested) existing.TryAdd(sk, sv?.DeepClone());
                }
                else if (!obj.ContainsKey(key))
                {
                    obj[key] = value?.DeepClone();
                }
            }
        }
        return obj;
    }

    // 递归按键名排序
    private static JsonNode SortKeys(JsonNode node)
    {
        if (node is JsonObject o)
        {
            var sorted = new JsonObject();
            foreach (var (k, v) in o.OrderBy(p => p.Key, StringComparer.Ordinal)) sorted[k] = v == null ? null : SortKeys(v.DeepClone());
            return sorted;
        }
        return node.DeepClone();
    }
}
