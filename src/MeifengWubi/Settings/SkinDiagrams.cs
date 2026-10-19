using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeifengWubi.Settings;

// 示意图共用：取主题色、画说明文字和尺寸箭头
internal static class DiagramInk
{
    public static Color Accent(FrameworkElement e) =>
        (e.TryFindResource("AccentFillColorDefaultBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0x67, 0xC0);

    public static Brush Secondary(FrameworkElement e) =>
        e.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;

    public static Brush Tertiary(FrameworkElement e) =>
        e.TryFindResource("TextFillColorTertiaryBrush") as Brush ?? Brushes.DarkGray;

    public static Brush Primary(FrameworkElement e) =>
        e.TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.Black;

    public static Brush Surface(FrameworkElement e) =>
        e.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;

    public static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);

    public static void Caption(DrawingContext dc, FrameworkElement e, string text, Point at, Brush brush, double size = 10, bool center = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(e).PixelsPerDip);
        var p = center ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at;
        dc.DrawText(ft, p);
    }

    // 两端带箭头的尺寸线
    public static void Dimension(DrawingContext dc, Brush brush, Point a, Point b)
    {
        dc.DrawLine(new Pen(brush, 1), a, b);
        ArrowHead(dc, brush, b, a);
        ArrowHead(dc, brush, a, b);
    }

    private static void ArrowHead(DrawingContext dc, Brush brush, Point tip, Point tail)
    {
        var angle = Math.Atan2(tip.Y - tail.Y, tip.X - tail.X);
        const double length = 5, spread = Math.PI / 7;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(new Point(tip.X - length * Math.Cos(angle - spread), tip.Y - length * Math.Sin(angle - spread)), true, false);
            ctx.LineTo(new Point(tip.X - length * Math.Cos(angle + spread), tip.Y - length * Math.Sin(angle + spread)), true, false);
        }
        dc.DrawGeometry(brush, null, g);
    }

    public static Pen Dashed(Brush brush, double width = 1, double dash = 4, double gap = 3) =>
        new(brush, width) { DashStyle = new DashStyle([dash / width, gap / width], 0) };
}

// 边距示意图：左上角为光标，箭头标出候选条相对光标的偏移；候选条内着色区域为内边距，控件摆在对应的边上
internal sealed class SpacingDiagram : Panel
{
    private const double CaretX = 22, CaretTop = 12, CaretHeight = 20;

    private readonly UIElement _top, _bottom, _left, _right, _offsetTop, _offsetLeft, _corner;
    private Rect _bar, _content;
    private double _arrowY;

    public SpacingDiagram(UIElement paddingTop, UIElement paddingBottom, UIElement paddingLeft, UIElement paddingRight,
        UIElement offsetTop, UIElement offsetLeft, UIElement cornerRadius)
    {
        (_top, _bottom, _left, _right, _offsetTop, _offsetLeft, _corner) =
            (paddingTop, paddingBottom, paddingLeft, paddingRight, offsetTop, offsetLeft, cornerRadius);
        foreach (var c in new[] { _top, _bottom, _left, _right, _offsetTop, _offsetLeft, _corner }) Children.Add(c);
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement c in Children) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var layout = Layout(double.IsInfinity(available.Width) ? 0 : available.Width);
        return new Size(Math.Max(layout.MinWidth, double.IsInfinity(available.Width) ? 0 : available.Width), layout.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var l = Layout(final.Width);
        foreach (var (el, at) in l.Places) el.Arrange(new Rect(at, el.DesiredSize));
        _bar = l.Bar;
        _content = l.Content;
        _arrowY = l.ArrowY;
        return final;
    }

    private (List<(UIElement, Point)> Places, Rect Bar, Rect Content, double ArrowY, double Height, double MinWidth) Layout(double width)
    {
        var places = new List<(UIElement, Point)>();
        var caretBottom = CaretTop + CaretHeight;
        var ot = _offsetTop.DesiredSize;
        var ol = _offsetLeft.DesiredSize;
        var pt = _top.DesiredSize;
        var pb = _bottom.DesiredSize;
        var pl = _left.DesiredSize;
        var pr = _right.DesiredSize;
        var cr = _corner.DesiredSize;

        var otPos = new Point(CaretX + 10, caretBottom + 8);
        var barTop = otPos.Y + ot.Height + 10;
        var barLeft = Math.Max(CaretX + 10 + ol.Width + 12, otPos.X + ot.Width + 8);
        var contentMinW = 110.0;
        var minWidth = barLeft + 8 + pl.Width + 8 + contentMinW + 8 + pr.Width + 8 + 16;
        var barRight = Math.Max(width, minWidth) - 16;
        var contentTop = barTop + 6 + pt.Height + 6;
        var contentH = Math.Max(34, Math.Max(pl.Height, pr.Height));
        var contentLeft = barLeft + 8 + pl.Width + 8;
        var contentRight = barRight - 8 - pr.Width - 8;
        var content = new Rect(contentLeft, contentTop, Math.Max(0, contentRight - contentLeft), contentH);
        var barBottom = content.Bottom + 6 + pb.Height + 6;
        var bar = new Rect(barLeft, barTop, barRight - barLeft, barBottom - barTop);

        places.Add((_offsetTop, otPos));
        var olPos = new Point(CaretX + 10, bar.Top + bar.Height / 2 + 12 - ol.Height / 2);
        places.Add((_offsetLeft, olPos));
        places.Add((_top, new Point(content.Left + content.Width / 2 - pt.Width / 2, barTop + 6)));
        places.Add((_bottom, new Point(content.Left + content.Width / 2 - pb.Width / 2, content.Bottom + 6)));
        places.Add((_left, new Point(barLeft + 8, content.Top + content.Height / 2 - pl.Height / 2)));
        places.Add((_right, new Point(barRight - 8 - pr.Width, content.Top + content.Height / 2 - pr.Height / 2)));
        places.Add((_corner, new Point(barRight - 10 - cr.Width, barTop + 6)));
        return (places, bar, content, olPos.Y - 6, barBottom + 14, minWidth);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bar.Width <= 0) return;
        var accent = DiagramInk.Accent(this);
        var accentBrush = new SolidColorBrush(accent);
        var caretBottom = CaretTop + CaretHeight;

        // 候选条：着色部分即内边距区域
        dc.DrawRoundedRectangle(new SolidColorBrush(DiagramInk.WithAlpha(accent, 0.10)), new Pen(new SolidColorBrush(DiagramInk.WithAlpha(accent, 0.55)), 1), _bar, 10, 10);
        DiagramInk.Caption(dc, this, "内边距", new Point(_bar.Left + 8, _bar.Top + 5), accentBrush);

        // 右上角加粗圆弧，提示旁边的数值对应圆角
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(new Point(_bar.Right - 10, _bar.Top), false, false);
            ctx.ArcTo(new Point(_bar.Right, _bar.Top + 10), new Size(10, 10), 0, false, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(accentBrush, 2.5), arc);

        // 内容区：虚线框 + 说明文字
        dc.DrawRoundedRectangle(DiagramInk.Surface(this), DiagramInk.Dashed(DiagramInk.Tertiary(this)), _content, 5, 5);
        DiagramInk.Caption(dc, this, "候选内容", new Point(_content.Left + _content.Width / 2, _content.Top + _content.Height / 2), DiagramInk.Secondary(this), 11, true);

        // 光标：I 形竖线
        var caretPen = new Pen(DiagramInk.Primary(this), 1.5);
        dc.DrawLine(caretPen, new Point(CaretX, CaretTop), new Point(CaretX, caretBottom));
        dc.DrawLine(caretPen, new Point(CaretX - 3, CaretTop), new Point(CaretX + 3, CaretTop));
        dc.DrawLine(caretPen, new Point(CaretX - 3, caretBottom), new Point(CaretX + 3, caretBottom));
        DiagramInk.Caption(dc, this, "光标", new Point(CaretX + 8, CaretTop + 3), DiagramInk.Secondary(this));

        // 虚线延长线：候选条顶边向左延到光标竖线，光标竖线向下延到水平箭头
        var guide = DiagramInk.Dashed(DiagramInk.Tertiary(this), 1, 3, 3);
        dc.DrawLine(guide, new Point(CaretX, _bar.Top), new Point(_bar.Left, _bar.Top));
        dc.DrawLine(guide, new Point(CaretX, _bar.Top), new Point(CaretX, _arrowY));

        // 尺寸箭头：竖向为距光标上，横向为距光标左
        DiagramInk.Dimension(dc, accentBrush, new Point(CaretX, caretBottom + 2), new Point(CaretX, _bar.Top - 1));
        DiagramInk.Dimension(dc, accentBrush, new Point(CaretX + 1, _arrowY), new Point(_bar.Left - 1, _arrowY));
    }
}

// 首选留白示意图：着色圆角块代表首选高亮背景，四个数值控件摆在对应的边上
internal sealed class HighlightPaddingDiagram : Panel
{
    private readonly UIElement _top, _bottom, _left, _right;
    private Rect _pill, _text;

    public HighlightPaddingDiagram(UIElement top, UIElement bottom, UIElement left, UIElement right)
    {
        (_top, _bottom, _left, _right) = (top, bottom, left, right);
        foreach (var c in new[] { top, bottom, left, right }) Children.Add(c);
        Width = 240;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement c in Children) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(240, Layout().Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var l = Layout();
        foreach (var (el, at) in l.Places) el.Arrange(new Rect(at, el.DesiredSize));
        _pill = l.Pill;
        _text = l.Text;
        return final;
    }

    private (List<(UIElement, Point)> Places, Rect Pill, Rect Text, double Height) Layout()
    {
        const double w = 240;
        var t = _top.DesiredSize;
        var b = _bottom.DesiredSize;
        var l = _left.DesiredSize;
        var r = _right.DesiredSize;
        var textTop = 2 + 5 + t.Height + 5;
        var textH = Math.Max(26, Math.Max(l.Height, r.Height));
        var textLeft = 2 + 8 + l.Width + 8;
        var textRight = w - 2 - 8 - r.Width - 8;
        var text = new Rect(textLeft, textTop, Math.Max(0, textRight - textLeft), textH);
        var pillBottom = text.Bottom + 5 + b.Height + 5;
        var pill = new Rect(2, 2, w - 4, pillBottom - 2);
        var cx = text.Left + text.Width / 2;
        var cy = text.Top + text.Height / 2;
        var places = new List<(UIElement, Point)>
        {
            (_top, new Point(cx - t.Width / 2, 2 + 5)),
            (_bottom, new Point(cx - b.Width / 2, text.Bottom + 5)),
            (_left, new Point(2 + 8, cy - l.Height / 2)),
            (_right, new Point(w - 2 - 8 - r.Width, cy - r.Height / 2))
        };
        return (places, pill, text, pillBottom + 2);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_pill.Width <= 0) return;
        var accent = DiagramInk.Accent(this);
        dc.DrawRoundedRectangle(new SolidColorBrush(DiagramInk.WithAlpha(accent, 0.16)), new Pen(new SolidColorBrush(DiagramInk.WithAlpha(accent, 0.6)), 1), _pill, 8, 8);
        // 中间虚线框代表首选文字
        dc.DrawRoundedRectangle(DiagramInk.Surface(this), DiagramInk.Dashed(DiagramInk.Tertiary(this)), _text, 4, 4);
        DiagramInk.Caption(dc, this, "1.五", new Point(_text.Left + _text.Width / 2, _text.Top + _text.Height / 2), DiagramInk.Secondary(this), 12, true);
    }
}
