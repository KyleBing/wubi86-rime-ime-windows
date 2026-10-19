using System.Windows;
using System.Windows.Media;
using Meifeng.Core.Config;
using Microsoft.Win32;

namespace MeifengWubi.Ui;

// 皮肤数值到 WPF 颜色、字体的转换，以及系统亮暗判断
internal static class SkinMedia
{
    public const string DefaultFontFamily = "Microsoft YaHei UI";

    // 设置页「候选字体」下拉里的常用字体（空串为系统默认）
    public static readonly (string Name, string Title)[] PreferredFonts =
    [
        ("", "系统默认"),
        ("Microsoft YaHei UI", "微软雅黑"),
        ("DengXian", "等线"),
        ("SimHei", "黑体"),
        ("SimSun", "宋体"),
        ("KaiTi", "楷体"),
        ("FangSong", "仿宋"),
        ("PingFang SC", "苹方")
    ];

    public static Color ToColor(this RGBAColor c)
    {
        static byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromArgb(B(c.Alpha), B(c.Red), B(c.Green), B(c.Blue));
    }

    public static RGBAColor ToRgba(this Color c) => new(c.R / 255.0, c.G / 255.0, c.B / 255.0, c.A / 255.0);

    public static SolidColorBrush ToBrush(this RGBAColor c)
    {
        var brush = new SolidColorBrush(c.ToColor());
        brush.Freeze();
        return brush;
    }

    // 皮肤字体名转成带回退的 FontFamily；macOS 皮肤里的苹方在 Windows 上回退到微软雅黑
    public static FontFamily FontFamilyFor(string fontName) =>
        new(string.IsNullOrWhiteSpace(fontName) ? DefaultFontFamily : $"{fontName}, {DefaultFontFamily}");

    public static Typeface TypefaceFor(string fontName, SkinFontWeight weight) =>
        new(FontFamilyFor(fontName), FontStyles.Normal, FontWeight.FromOpenTypeWeight(weight.OpenTypeWeight()), FontStretches.Normal);

    // 字体实际提供的字重；查不到时列出全部档位
    public static List<SkinFontWeight> AvailableWeights(string fontName)
    {
        var all = Enum.GetValues<SkinFontWeight>().ToList();
        try
        {
            var family = new FontFamily(string.IsNullOrWhiteSpace(fontName) ? DefaultFontFamily : fontName);
            var weights = family.FamilyTypefaces.Select(t => t.Weight.ToOpenTypeWeight()).Distinct().ToList();
            if (weights.Count == 0) return all;
            var result = all.Where(w => weights.Contains(w.OpenTypeWeight())).ToList();
            return result.Count == 0 ? all : result;
        }
        catch
        {
            return all;
        }
    }

    // 系统是否为深色应用模式
    public static bool SystemIsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
