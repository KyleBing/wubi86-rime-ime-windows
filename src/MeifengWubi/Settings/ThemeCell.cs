using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Meifeng.Core.Config;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 主题卡片：缩略图 + 名称；点击应用，存储位额外带「存入当前」与清空按钮
internal sealed class ThemeCell : Border
{
    public CandidateSkin? Skin { get; }
    public Action? OnApply { get; set; }
    public Action? OnSave { get; set; }
    public Action? OnClear { get; set; }

    public ThemeCell(CandidateSkin? skin, string title, string caption, bool isSlot)
    {
        Skin = skin;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(7);
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");

        var thumbnail = new SkinThumbnail(skin) { Height = 34 };
        var titleLabel = UiKit.Label(title, 12, FontWeights.Medium);
        titleLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        if (skin == null) titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        var captionLabel = UiKit.Label(caption, 10, FontWeights.Medium, secondary: true);
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 5, 0, 0) };
        if (isSlot && skin != null)
        {
            var clear = UiKit.TextButton("\uE711", () => OnClear?.Invoke());
            clear.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            clear.FontSize = 10;
            clear.ToolTip = "清空此位置";
            DockPanel.SetDock(clear, Dock.Right);
            header.Children.Add(clear);
        }
        captionLabel.Margin = new Thickness(4, 0, 0, 0);
        DockPanel.SetDock(captionLabel, Dock.Right);
        header.Children.Add(captionLabel);
        header.Children.Add(titleLabel);

        var stack = new StackPanel();
        stack.Children.Add(thumbnail);
        stack.Children.Add(header);
        if (isSlot)
        {
            var save = UiKit.Button("存入当前", () => OnSave?.Invoke());
            save.FontSize = 11;
            save.HorizontalAlignment = HorizontalAlignment.Stretch;
            save.Margin = new Thickness(0, 5, 0, 0);
            stack.Children.Add(save);
        }
        Child = stack;
        ToolTip = skin == null ? "空位：点击「存入当前」保存正在使用的外观" : $"点击应用「{title}」";
        if (skin != null) Cursor = Cursors.Hand;
        MouseLeftButtonUp += (_, e) =>
        {
            if (Skin == null || e.OriginalSource is DependencyObject d && IsInsideButton(d)) return;
            OnApply?.Invoke();
        };
        SetCurrent(false);
    }

    // 当前使用中的主题用强调色描边
    public void SetCurrent(bool current)
    {
        BorderThickness = new Thickness(current ? 2 : 1);
        SetResourceReference(BorderBrushProperty, current ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");
    }

    private bool IsInsideButton(DependencyObject d)
    {
        for (var cur = d; cur != null && cur != this; cur = VisualTreeHelper.GetParent(cur))
        {
            if (cur is Button) return true;
        }
        return false;
    }
}

// 主题缩略图：按主题配色画一条迷你候选条（跟随系统亮暗），空位画虚线框
internal sealed class SkinThumbnail(CandidateSkin? skin) : FrameworkElement
{
    private static readonly string[] Samples = ["五", "吾", "伍", "捂"];

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(1, 3, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 6));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (skin == null)
        {
            dc.DrawRoundedRectangle(null, DiagramInk.Dashed(DiagramInk.Tertiary(this)), rect, 6, 6);
            DiagramInk.Caption(dc, this, "未保存", new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), DiagramInk.Tertiary(this), 11, true);
            return;
        }
        var colors = skin.ColorsFor(SkinMedia.SystemIsDark);
        var radius = Math.Min(skin.CornerRadius, rect.Height / 2);
        var border = skin.BorderWidth > 0 ? new Pen(colors.Border.ToBrush(), Math.Min(skin.BorderWidth, 2)) : null;
        dc.DrawRoundedRectangle(colors.Background.ToBrush(), border, rect, radius, radius);

        // 固定缩略字号，只体现配色、高亮与字重
        var x = rect.Left + 5;
        var midY = rect.Top + rect.Height / 2;
        for (var i = 0; i < Samples.Length; i++)
        {
            var highlighted = skin.HighlightEnabled && i == 0;
            var index = Text($"{i + 1}", 8, skin.IndexWeight, highlighted ? colors.HighlightIndexText : colors.IndexText, dpi);
            var word = Text(Samples[i], 12, highlighted ? skin.HighlightWeight : skin.CandidateWeight, highlighted ? colors.HighlightText : colors.CandidateText, dpi);
            var itemWidth = index.Width + 2 + word.Width + 8;
            if (x + itemWidth > rect.Right - 3) break;
            if (highlighted)
            {
                var pr = Math.Min(skin.HighlightCornerRadius, 9);
                dc.DrawRoundedRectangle(colors.HighlightBackground.ToBrush(), null, new Rect(x, midY - 9, itemWidth, 18), pr, pr);
            }
            dc.DrawText(index, new Point(x + 4, midY - index.Height / 2));
            dc.DrawText(word, new Point(x + 4 + index.Width + 2, midY - word.Height / 2));
            x += itemWidth + 2;
        }
    }

    private FormattedText Text(string s, double size, SkinFontWeight weight, RGBAColor color, double dpi) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, SkinMedia.TypefaceFor("", weight), size, color.ToBrush(), dpi);
}
