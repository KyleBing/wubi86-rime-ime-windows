using System.Text.Json.Serialization;

namespace Meifeng.Core.Config;

// 字重档位，JSON 值与 macOS 版一致（ultraLight / thin / ...）
public enum SkinFontWeight
{
    UltraLight,
    Thin,
    Light,
    Regular,
    Medium,
    Semibold,
    Bold,
    Heavy,
    Black
}

// 行内序号与候选词的对齐方式；靠下按基线对齐
public enum SkinVerticalAlignment
{
    Top,
    Center,
    Bottom
}

public static class SkinEnumTitles
{
    public static string Title(this SkinFontWeight w) => w switch
    {
        SkinFontWeight.UltraLight => "极细",
        SkinFontWeight.Thin => "纤细",
        SkinFontWeight.Light => "细体",
        SkinFontWeight.Regular => "常规",
        SkinFontWeight.Medium => "中等",
        SkinFontWeight.Semibold => "半粗",
        SkinFontWeight.Bold => "粗体",
        SkinFontWeight.Heavy => "特粗",
        _ => "黑体"
    };

    // 对应 OpenType 字重数值（100–900）
    public static int OpenTypeWeight(this SkinFontWeight w) => w switch
    {
        SkinFontWeight.UltraLight => 100,
        SkinFontWeight.Thin => 200,
        SkinFontWeight.Light => 300,
        SkinFontWeight.Regular => 400,
        SkinFontWeight.Medium => 500,
        SkinFontWeight.Semibold => 600,
        SkinFontWeight.Bold => 700,
        SkinFontWeight.Heavy => 800,
        _ => 900
    };

    public static string Title(this SkinVerticalAlignment a) => a switch
    {
        SkinVerticalAlignment.Top => "靠上",
        SkinVerticalAlignment.Center => "居中",
        _ => "靠下"
    };
}

// 0–1 的 sRGB 颜色
public sealed record RGBAColor(
    [property: JsonPropertyName("red")] double Red,
    [property: JsonPropertyName("green")] double Green,
    [property: JsonPropertyName("blue")] double Blue,
    [property: JsonPropertyName("alpha")] double Alpha = 1)
{
    public RGBAColor Normalized() => new(Math.Clamp(Red, 0, 1), Math.Clamp(Green, 0, 1), Math.Clamp(Blue, 0, 1), Math.Clamp(Alpha, 0, 1));

    public static RGBAColor Gray(double v, double alpha = 1) => new(v, v, v, alpha);

    // 转成 #AARRGGBB 便于界面显示
    public string ToHex()
    {
        static int B(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{B(Alpha):X2}{B(Red):X2}{B(Green):X2}{B(Blue):X2}";
    }
}

// 一套明或暗配色
public sealed record SkinColors(
    [property: JsonPropertyName("background")] RGBAColor Background,
    [property: JsonPropertyName("candidateText")] RGBAColor CandidateText,
    [property: JsonPropertyName("indexText")] RGBAColor IndexText,
    [property: JsonPropertyName("border")] RGBAColor Border,
    [property: JsonPropertyName("shadow")] RGBAColor Shadow,
    [property: JsonPropertyName("highlightBackground")] RGBAColor HighlightBackground,
    [property: JsonPropertyName("highlightText")] RGBAColor HighlightText,
    [property: JsonPropertyName("highlightIndexText")] RGBAColor HighlightIndexText,
    [property: JsonPropertyName("codeText")] RGBAColor CodeText,
    [property: JsonPropertyName("highlightCodeText")] RGBAColor HighlightCodeText)
{
    public SkinColors Normalized() => new(
        Background.Normalized(), CandidateText.Normalized(), IndexText.Normalized(), Border.Normalized(),
        Shadow.Normalized(), HighlightBackground.Normalized(), HighlightText.Normalized(),
        HighlightIndexText.Normalized(), CodeText.Normalized(), HighlightCodeText.Normalized());
}

// 可复制分享的候选栏皮肤。尺寸配置共用，颜色按浅色和深色分别保存。JSON 与 macOS 版互通
public sealed record CandidateSkin
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")] public int Version { get; init; } = CurrentVersion;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("author")] public string Author { get; init; } = "";
    [JsonPropertyName("fontName")] public string FontName { get; init; } = "";
    [JsonPropertyName("paddingTop")] public double PaddingTop { get; init; }
    [JsonPropertyName("paddingBottom")] public double PaddingBottom { get; init; }
    [JsonPropertyName("paddingLeft")] public double PaddingLeft { get; init; }
    [JsonPropertyName("paddingRight")] public double PaddingRight { get; init; }
    // 候选条相对光标的垂直间距（面板在光标下方时为上边距）
    [JsonPropertyName("offsetTop")] public double OffsetTop { get; init; }
    // 候选条相对光标的水平偏移（正值向右）
    [JsonPropertyName("offsetLeft")] public double OffsetLeft { get; init; }
    [JsonPropertyName("cornerRadius")] public double CornerRadius { get; init; }
    [JsonPropertyName("verticalAlignment")] public SkinVerticalAlignment VerticalAlignment { get; init; } = SkinVerticalAlignment.Center;
    [JsonPropertyName("itemSpacing")] public double ItemSpacing { get; init; }
    // 序号与候选词之间的水平间距
    [JsonPropertyName("indexSpacing")] public double IndexSpacing { get; init; }
    [JsonPropertyName("candidateFontSize")] public double CandidateFontSize { get; init; }
    [JsonPropertyName("candidateWeight")] public SkinFontWeight CandidateWeight { get; init; } = SkinFontWeight.Regular;
    [JsonPropertyName("indexFontSize")] public double IndexFontSize { get; init; }
    [JsonPropertyName("indexWeight")] public SkinFontWeight IndexWeight { get; init; } = SkinFontWeight.Regular;
    // 词条后面的五笔编码字号
    [JsonPropertyName("codeFontSize")] public double CodeFontSize { get; init; }
    [JsonPropertyName("borderWidth")] public double BorderWidth { get; init; }
    [JsonPropertyName("shadowEnabled")] public bool ShadowEnabled { get; init; }
    [JsonPropertyName("shadowRadius")] public double ShadowRadius { get; init; }
    [JsonPropertyName("shadowOffsetX")] public double ShadowOffsetX { get; init; }
    // 与 macOS 一致：负值表示阴影向下
    [JsonPropertyName("shadowOffsetY")] public double ShadowOffsetY { get; init; }
    // 首位候选（空格上屏）是否使用单独的高亮样式
    [JsonPropertyName("highlightEnabled")] public bool HighlightEnabled { get; init; }
    [JsonPropertyName("highlightCornerRadius")] public double HighlightCornerRadius { get; init; }
    // 高亮背景四边留白；开启后所有候选统一留白，保证间距与行高一致
    [JsonPropertyName("highlightPaddingTop")] public double HighlightPaddingTop { get; init; }
    [JsonPropertyName("highlightPaddingBottom")] public double HighlightPaddingBottom { get; init; }
    [JsonPropertyName("highlightPaddingLeft")] public double HighlightPaddingLeft { get; init; }
    [JsonPropertyName("highlightPaddingRight")] public double HighlightPaddingRight { get; init; }
    [JsonPropertyName("highlightWeight")] public SkinFontWeight HighlightWeight { get; init; } = SkinFontWeight.Medium;
    [JsonPropertyName("light")] public SkinColors Light { get; init; } = DefaultLight;
    [JsonPropertyName("dark")] public SkinColors Dark { get; init; } = DefaultDark;

    private static readonly SkinColors DefaultLight = new(
        new RGBAColor(0.98, 0.98, 0.99, 0.92),
        new RGBAColor(0.08, 0.08, 0.09),
        new RGBAColor(0.38, 0.38, 0.42),
        new RGBAColor(0, 0, 0, 0.16),
        new RGBAColor(0, 0, 0, 0.28),
        new RGBAColor(0.20, 0.47, 0.96),
        new RGBAColor(1, 1, 1),
        new RGBAColor(1, 1, 1, 0.78),
        RGBAColor.Gray(0.614888698630137),
        RGBAColor.Gray(0.3245130565068493));

    private static readonly SkinColors DefaultDark = new(
        new RGBAColor(0.13, 0.13, 0.15, 0.94),
        new RGBAColor(0.95, 0.95, 0.97),
        new RGBAColor(0.68, 0.68, 0.72),
        new RGBAColor(1, 1, 1, 0.18),
        new RGBAColor(0, 0, 0, 0.58),
        new RGBAColor(0.26, 0.53, 0.98),
        new RGBAColor(1, 1, 1),
        new RGBAColor(1, 1, 1, 0.78),
        RGBAColor.Gray(0.7359364297945206),
        RGBAColor.Gray(0.7462810359589042));

    // 未保存皮肤时用内置简约黑白，恢复默认和设置页的当前主题描边也按这一套
    public static CandidateSkin Default => MonochromePreset;

    // 内置主题：只读，应用时保留用户当前字体
    public static IReadOnlyList<CandidateSkin> Presets => [CoralPreset, MonochromePreset];

    // 珊瑚红与简约黑白共用的尺寸。单独构造，避免 Default 再指回简约黑白时循环引用
    private static CandidateSkin CompactBase => new()
    {
        Author = "玫枫五笔",
        FontName = "",
        PaddingTop = 4,
        PaddingBottom = 4,
        PaddingLeft = 5,
        PaddingRight = 5,
        OffsetTop = 4,
        OffsetLeft = 0,
        CornerRadius = 7,
        VerticalAlignment = SkinVerticalAlignment.Center,
        ItemSpacing = 3,
        IndexSpacing = 6,
        CandidateFontSize = 18,
        CandidateWeight = SkinFontWeight.Regular,
        IndexFontSize = 11,
        IndexWeight = SkinFontWeight.Light,
        CodeFontSize = 12,
        BorderWidth = 0.5,
        ShadowEnabled = true,
        ShadowRadius = 4,
        ShadowOffsetX = 2,
        ShadowOffsetY = -2,
        HighlightEnabled = true,
        HighlightCornerRadius = 5,
        HighlightPaddingTop = 1,
        HighlightPaddingBottom = 1,
        HighlightPaddingLeft = 3,
        HighlightPaddingRight = 3,
        HighlightWeight = SkinFontWeight.Regular,
        Light = DefaultLight,
        Dark = DefaultDark
    };

    // 珊瑚红：浅色白底、深色近黑底，首选珊瑚红高亮
    private static CandidateSkin CoralPreset
    {
        get
        {
            var coral = new RGBAColor(0.96, 0.377, 0.377);
            return CompactBase with
            {
                Name = "珊瑚红",
                Light = new SkinColors(
                    new RGBAColor(1, 1, 1),
                    new RGBAColor(0.283, 0.283, 0.318),
                    new RGBAColor(0.38, 0.38, 0.42),
                    new RGBAColor(0, 0, 0, 0.16),
                    new RGBAColor(0, 0, 0, 0.28),
                    coral,
                    new RGBAColor(1, 1, 1),
                    new RGBAColor(1, 1, 1, 0.78),
                    RGBAColor.Gray(0.614888698630137),
                    new RGBAColor(1, 1, 1)),
                Dark = new SkinColors(
                    new RGBAColor(0.1355506262357012, 0.1355506262357012, 0.138404323630137, 0.94),
                    new RGBAColor(0.95, 0.95, 0.97),
                    new RGBAColor(0.68, 0.68, 0.72),
                    new RGBAColor(1, 1, 1, 0.18),
                    new RGBAColor(0, 0, 0, 0.58),
                    coral,
                    new RGBAColor(1, 1, 1),
                    new RGBAColor(1, 1, 1, 0.78),
                    RGBAColor.Gray(0.7359364297945206),
                    new RGBAColor(1, 1, 1))
            };
        }
    }

    // 简约黑白：白底 / 深灰底，首选与背景同色，靠文字颜色区分
    private static CandidateSkin MonochromePreset => CompactBase with
    {
        Name = "简约黑白",
        PaddingTop = 3,
        PaddingBottom = 3,
        Light = new SkinColors(
            new RGBAColor(1, 1, 1),
            new RGBAColor(0.283, 0.283, 0.318),
            new RGBAColor(0.38, 0.38, 0.42),
            new RGBAColor(0, 0, 0, 0.16),
            new RGBAColor(0, 0, 0, 0.28),
            new RGBAColor(1, 1, 1),
            new RGBAColor(0, 0, 0),
            new RGBAColor(0.243, 0.247, 0.247, 0.695),
            RGBAColor.Gray(0.614888698630137),
            RGBAColor.Gray(0.3245130565068493)),
        Dark = new SkinColors(
            RGBAColor.Gray(0.13736087328767124, 0.94),
            new RGBAColor(0.8973309957986161, 0.8973309957986161, 0.9162221746575343),
            new RGBAColor(0.68, 0.68, 0.72),
            new RGBAColor(1, 1, 1, 0.18),
            new RGBAColor(0, 0, 0, 0.58),
            RGBAColor.Gray(0.1409193065068493),
            new RGBAColor(1, 1, 1),
            new RGBAColor(1, 1, 1, 0.68),
            RGBAColor.Gray(0.7359364297945206),
            RGBAColor.Gray(0.7462810359589042))
    };

    // 比较外观是否一致（忽略字体，内置主题应用时会沿用当前字体）
    public bool LooksSame(CandidateSkin other) =>
        Normalized() with { FontName = "" } == other.Normalized() with { FontName = "" };

    // 导入后统一收敛数值，避免异常配置生成不可操作的候选窗
    public CandidateSkin Normalized() => this with
    {
        Version = CurrentVersion,
        Name = Truncate(Name, 80),
        Author = Truncate(Author, 80),
        FontName = Truncate(FontName, 160),
        PaddingTop = Math.Clamp(PaddingTop, 0, 30),
        PaddingBottom = Math.Clamp(PaddingBottom, 0, 30),
        PaddingLeft = Math.Clamp(PaddingLeft, 0, 40),
        PaddingRight = Math.Clamp(PaddingRight, 0, 40),
        OffsetTop = Math.Clamp(OffsetTop, -40, 80),
        OffsetLeft = Math.Clamp(OffsetLeft, -80, 80),
        CornerRadius = Math.Clamp(CornerRadius, 0, 30),
        ItemSpacing = Math.Clamp(ItemSpacing, 0, 30),
        IndexSpacing = Math.Clamp(IndexSpacing, 0, 20),
        CandidateFontSize = Math.Clamp(CandidateFontSize, 9, 40),
        IndexFontSize = Math.Clamp(IndexFontSize, 8, 32),
        CodeFontSize = Math.Clamp(CodeFontSize, 8, 32),
        BorderWidth = Math.Clamp(BorderWidth, 0, 6),
        ShadowRadius = Math.Clamp(ShadowRadius, 0, 40),
        ShadowOffsetX = Math.Clamp(ShadowOffsetX, -30, 30),
        ShadowOffsetY = Math.Clamp(ShadowOffsetY, -30, 30),
        HighlightCornerRadius = Math.Clamp(HighlightCornerRadius, 0, 20),
        HighlightPaddingTop = Math.Clamp(HighlightPaddingTop, 0, 20),
        HighlightPaddingBottom = Math.Clamp(HighlightPaddingBottom, 0, 20),
        HighlightPaddingLeft = Math.Clamp(HighlightPaddingLeft, 0, 20),
        HighlightPaddingRight = Math.Clamp(HighlightPaddingRight, 0, 20),
        Light = Light.Normalized(),
        Dark = Dark.Normalized()
    };

    public SkinColors ColorsFor(bool dark) => dark ? Dark : Light;

    // 候选行留白：开启首选高亮时四边生效，关闭时为零
    public (double Top, double Left, double Bottom, double Right) HighlightInsets =>
        HighlightEnabled ? (HighlightPaddingTop, HighlightPaddingLeft, HighlightPaddingBottom, HighlightPaddingRight) : (0, 0, 0, 0);

    private static string Truncate(string s, int n)
    {
        var info = new System.Globalization.StringInfo(s ?? "");
        return info.LengthInTextElements <= n ? s ?? "" : info.SubstringByTextElements(0, n);
    }
}
