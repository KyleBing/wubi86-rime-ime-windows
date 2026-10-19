using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Meifeng.Core.Config;
using MeifengWubi.Ui;

namespace MeifengWubi.Candidate;

// 候选条的显示选项（来自外观偏好）
internal sealed record CandidateBarOptions(
    CandidateOrientation Orientation,
    IndexStyle IndexStyle,
    CandidateDisplayMode DisplayMode,
    bool ShowCode)
{
    public static CandidateBarOptions Current => new(
        AppearancePrefs.Orientation, AppearancePrefs.IndexStyle, AppearancePrefs.DisplayMode, AppearancePrefs.ShowPreeditInPanel);
}

// 一次排版的结果：根元素（含阴影外扩）、整体尺寸、各候选的可点区域（DIP，相对根元素左上角）
internal sealed record CandidateBarResult(FrameworkElement Root, Size Size, double ShadowOutset, IReadOnlyList<Rect> HitRects);

// 候选条排版：实际候选窗与设置页预览共用，保证两边尺寸、内边距完全一致（对应 macOS CandidateBarLayout）
internal static class CandidateBarRenderer
{
    // 圆点模式尺寸：圆点直径、间距与整条高度
    public const double DotDiameter = 4;
    public const double DotSpacing = 2.5;
    public const double DotBarHeight = 8;

    // 仅圆点时第一个点固定珊瑚红，与珊瑚红主题高亮同一色
    private static readonly RGBAColor FirstDotColor = new(0.96, 0.377, 0.377);

    // 阴影外扩：高斯模糊的衰减范围约为模糊半径的 2 倍
    public static double ShadowOutset(CandidateSkin skin, CandidateBarOptions options, bool reserve)
    {
        if (!reserve || options.DisplayMode == CandidateDisplayMode.Dots || !skin.ShadowEnabled) return 0;
        var offset = Math.Max(Math.Abs(skin.ShadowOffsetX), Math.Abs(skin.ShadowOffsetY));
        return Math.Ceiling(skin.ShadowRadius * 2 + offset + 2);
    }

    // 按皮肤排版一页候选；pixelsPerDip 用于文字度量
    public static CandidateBarResult Build(CandidateSkin skin, SkinColors colors, IReadOnlyList<string> texts,
        IReadOnlyList<string> codes, CandidateBarOptions options, bool reserveShadow, double pixelsPerDip)
    {
        var dots = options.DisplayMode == CandidateDisplayMode.Dots;
        var outset = ShadowOutset(skin, options, reserveShadow);
        var content = new DrawingGroup();
        var hits = new List<Rect>();
        Size bar;

        using (var dc = content.Open())
        {
            bar = dots
                ? DrawDots(dc, texts.Count, colors, hits)
                : DrawItems(dc, skin, colors, texts, codes, options, pixelsPerDip, hits);
        }

        var size = new Size(bar.Width + outset * 2, bar.Height + outset * 2);
        var radius = dots ? DotBarHeight / 2 : skin.CornerRadius;
        var root = new Canvas { Width = size.Width, Height = size.Height, SnapsToDevicePixels = false };
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);

        // 背景与边框；阴影挂在背景上，形状与面板一致
        var chrome = new Border
        {
            Width = bar.Width,
            Height = bar.Height,
            CornerRadius = new CornerRadius(radius),
            Background = colors.Background.ToBrush(),
            BorderBrush = colors.Border.ToBrush(),
            BorderThickness = new Thickness(dots ? 0 : skin.BorderWidth)
        };
        if (outset > 0)
        {
            var dx = skin.ShadowOffsetX;
            var dy = skin.ShadowOffsetY;
            chrome.Effect = new DropShadowEffect
            {
                Color = colors.Shadow.ToColor() with { A = 255 },
                Opacity = colors.Shadow.Alpha,
                BlurRadius = skin.ShadowRadius * 1.6,
                ShadowDepth = Math.Sqrt(dx * dx + dy * dy),
                // macOS 的 Y 轴向上为正，WPF 的方向角 90° 也朝上
                Direction = Math.Atan2(dy, dx) * 180 / Math.PI,
                RenderingBias = RenderingBias.Quality
            };
        }
        Canvas.SetLeft(chrome, outset);
        Canvas.SetTop(chrome, outset);
        root.Children.Add(chrome);

        var drawing = new Image { Source = new DrawingImage(content), Width = bar.Width, Height = bar.Height, Stretch = Stretch.None };
        // DrawingImage 以内容边界为原点，用透明底框把原点固定在 (0,0)
        Canvas.SetLeft(drawing, outset);
        Canvas.SetTop(drawing, outset);
        root.Children.Add(drawing);

        var shifted = hits.Select(r => new Rect(r.X + outset, r.Y + outset, r.Width, r.Height)).ToList();
        return new CandidateBarResult(root, size, outset, shifted);
    }

    // 把排版结果栅格化为预乘 Alpha 位图（物理像素）
    public static BitmapSource Rasterize(FrameworkElement root, Size size, double scale)
    {
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var w = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
        var h = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
        var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }

    // 数量圆点：整条高 8，4 圆点 + 上下各 2
    private static Size DrawDots(DrawingContext dc, int count, SkinColors colors, List<Rect> hits)
    {
        var inset = (DotBarHeight - DotDiameter) / 2;
        var left = DotBarHeight / 2;
        var width = left * 2 + count * DotDiameter + Math.Max(0, count - 1) * DotSpacing;
        // 透明底框固定绘图原点
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, DotBarHeight));
        for (var i = 0; i < count; i++)
        {
            var x = left + i * (DotDiameter + DotSpacing);
            var color = i == 0 ? FirstDotColor : colors.CandidateText with { Alpha = colors.CandidateText.Alpha * 0.85 };
            dc.DrawEllipse(color.ToBrush(), null, new Point(x + DotDiameter / 2, inset + DotDiameter / 2), DotDiameter / 2, DotDiameter / 2);
            hits.Add(new Rect(x - DotSpacing / 2, 0, DotDiameter + DotSpacing, DotBarHeight));
        }
        return new Size(width, DotBarHeight);
    }

    private sealed record ItemLayout(FormattedText? Index, FormattedText Word, FormattedText? Code, double Width, double TextHeight, double MaxBaseline);

    private static Size DrawItems(DrawingContext dc, CandidateSkin skin, SkinColors colors, IReadOnlyList<string> texts,
        IReadOnlyList<string> codes, CandidateBarOptions options, double ppd, List<Rect> hits)
    {
        var vertical = options.Orientation == CandidateOrientation.Vertical;
        var showCode = options.ShowCode;
        var rowHeight = Math.Ceiling(Math.Max(Math.Max(skin.CandidateFontSize, skin.IndexFontSize), showCode ? skin.CodeFontSize : 0) * 1.35);
        var highlightOn = skin.HighlightEnabled;
        var inset = skin.HighlightInsets;
        var boxHeight = rowHeight + inset.Top + inset.Bottom;

        // 先量出每项宽度，竖排开启高亮时整列统一宽度
        var items = new List<ItemLayout>();
        for (var i = 0; i < texts.Count; i++)
        {
            var highlighted = highlightOn && i == 0;
            FormattedText? index = null;
            if (options.IndexStyle.Label(i + 1) is { } label)
            {
                index = Text(label, skin.FontName, skin.IndexFontSize, skin.IndexWeight,
                    highlighted ? colors.HighlightIndexText : colors.IndexText, ppd);
            }
            var word = Text(texts[i], skin.FontName, skin.CandidateFontSize, highlighted ? skin.HighlightWeight : skin.CandidateWeight,
                highlighted ? colors.HighlightText : colors.CandidateText, ppd);
            var codeText = i < codes.Count ? codes[i] : "";
            FormattedText? code = showCode && codeText.Length > 0
                ? Text(codeText, skin.FontName, skin.CodeFontSize, SkinFontWeight.Regular, highlighted ? colors.HighlightCodeText : colors.CodeText, ppd)
                : null;
            // 编码紧跟词条，间距固定 4，不跟序号间距走
            var pairWidth = word.WidthIncludingTrailingWhitespace + (code != null ? 4 + code.WidthIncludingTrailingWhitespace : 0);
            var width = (index != null ? index.WidthIncludingTrailingWhitespace + skin.IndexSpacing : 0) + pairWidth;
            var fields = new[] { index, word, code }.Where(f => f != null).Cast<FormattedText>().ToList();
            var maxBaseline = fields.Max(f => f.Baseline);
            var textHeight = skin.VerticalAlignment == SkinVerticalAlignment.Bottom
                ? maxBaseline + fields.Max(f => f.Height - f.Baseline)
                : fields.Max(f => f.Height);
            items.Add(new ItemLayout(index, word, code, width, textHeight, maxBaseline));
        }

        var fillWidth = vertical && highlightOn;
        var columnWidth = items.Count == 0 ? 0 : items.Max(it => inset.Left + it.Width + inset.Right);

        double x = skin.PaddingLeft, y = skin.PaddingTop, maxX = 0, maxY = 0;
        // 先记录各项位置，背景要先画再画文字
        var boxes = new List<Rect>();
        foreach (var it in items)
        {
            var w = fillWidth ? columnWidth : inset.Left + it.Width + inset.Right;
            var box = new Rect(x, y, w, boxHeight);
            boxes.Add(box);
            if (vertical)
            {
                y += boxHeight + skin.ItemSpacing;
            }
            else
            {
                x += w + skin.ItemSpacing;
            }
            maxX = Math.Max(maxX, box.Right);
            maxY = Math.Max(maxY, box.Bottom);
        }
        var barWidth = (items.Count == 0 ? skin.PaddingLeft : maxX) + skin.PaddingRight;
        var barHeight = (items.Count == 0 ? skin.PaddingTop + boxHeight : maxY) + skin.PaddingBottom;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, barWidth, barHeight));
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var box = boxes[i];
            hits.Add(box);
            if (highlightOn && i == 0)
            {
                dc.DrawRoundedRectangle(colors.HighlightBackground.ToBrush(), null, box, skin.HighlightCornerRadius, skin.HighlightCornerRadius);
            }
            // 文字在行内的纵向位置：靠上 / 靠下贴住留白；居中时汉字光学中心略偏上，下移 0.5 视觉更居中
            var textTop = skin.VerticalAlignment switch
            {
                SkinVerticalAlignment.Top => box.Top + inset.Top,
                SkinVerticalAlignment.Bottom => box.Bottom - inset.Bottom - it.TextHeight,
                _ => box.Top + (box.Height - it.TextHeight) / 2 + 0.5 + (inset.Top - inset.Bottom) / 2
            };
            var cx = box.Left + inset.Left;
            if (it.Index != null)
            {
                dc.DrawText(it.Index, new Point(cx, FieldTop(it.Index, it, textTop, skin.VerticalAlignment)));
                cx += it.Index.WidthIncludingTrailingWhitespace + skin.IndexSpacing;
            }
            dc.DrawText(it.Word, new Point(cx, FieldTop(it.Word, it, textTop, skin.VerticalAlignment)));
            cx += it.Word.WidthIncludingTrailingWhitespace;
            if (it.Code != null)
            {
                cx += 4;
                dc.DrawText(it.Code, new Point(cx, FieldTop(it.Code, it, textTop, skin.VerticalAlignment)));
            }
        }
        return new Size(barWidth, barHeight);
    }

    // 同一行内各段文字的纵向对齐：靠上齐顶、居中齐中线、靠下齐基线
    private static double FieldTop(FormattedText field, ItemLayout it, double textTop, SkinVerticalAlignment alignment) => alignment switch
    {
        SkinVerticalAlignment.Top => textTop,
        SkinVerticalAlignment.Bottom => textTop + it.MaxBaseline - field.Baseline,
        _ => textTop + (it.TextHeight - field.Height) / 2
    };

    private static FormattedText Text(string s, string font, double size, SkinFontWeight weight, RGBAColor color, double ppd) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, SkinMedia.TypefaceFor(font, weight), size, color.ToBrush(), ppd);
}
