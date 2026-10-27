using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MeifengWubi.Ui;

// 状态文字的语气：闲置灰、成功绿、失败红、需处理橙
internal enum Tone { Idle, Success, Error, Attention }

// 设置界面的通用控件构造（对应 macOS 各设置页里的 card / formRow 等辅助方法）
internal static class UiKit
{
    public static readonly Brush SuccessBrush = Frozen(Color.FromRgb(0x10, 0x8A, 0x3C));
    public static readonly Brush ErrorBrush = Frozen(Color.FromRgb(0xD1, 0x34, 0x38));
    public static readonly Brush AttentionBrush = Frozen(Color.FromRgb(0xC8, 0x6A, 0x00));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static TextBlock Label(string text, double size = 12, FontWeight? weight = null, bool secondary = false, bool wrap = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (secondary) t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return t;
    }

    // 可选中复制的说明文字
    public static TextBox SelectableText(string text, double size = 12, bool mono = false)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 0
        };
        if (mono) box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        return box;
    }

    public static void SetTone(TextBlock label, string text, Tone tone)
    {
        label.Text = text;
        label.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        switch (tone)
        {
            case Tone.Success: label.Foreground = SuccessBrush; break;
            case Tone.Error: label.Foreground = ErrorBrush; break;
            case Tone.Attention: label.Foreground = AttentionBrush; break;
            default: label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"); break;
        }
    }

    public static StackPanel VStack(double spacing, params UIElement[] children)
    {
        var s = new StackPanel { Orientation = Orientation.Vertical };
        AddSpaced(s, spacing, children);
        return s;
    }

    public static StackPanel HStack(double spacing, params UIElement[] children)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        AddSpaced(s, spacing, children);
        return s;
    }

    // 依次加入子元素，用外边距做间距
    public static void AddSpaced(Panel panel, double spacing, IEnumerable<UIElement> children)
    {
        var horizontal = panel is StackPanel { Orientation: Orientation.Horizontal };
        var first = panel.Children.Count == 0;
        foreach (var child in children)
        {
            if (!first && child is FrameworkElement fe)
            {
                var m = fe.Margin;
                fe.Margin = horizontal ? new Thickness(m.Left + spacing, m.Top, m.Right, m.Bottom) : new Thickness(m.Left, m.Top + spacing, m.Right, m.Bottom);
            }
            panel.Children.Add(child);
            first = false;
        }
    }

    // 圆角卡片：标题、说明、内容
    public static Border Card(string? title, string? subtitle, UIElement content)
    {
        var stack = new StackPanel();
        if (title != null) stack.Children.Add(Label(title, 13, FontWeights.SemiBold));
        if (subtitle != null)
        {
            var sub = Label(subtitle, 11, secondary: true, wrap: true);
            sub.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(sub);
        }
        if (content is FrameworkElement fe && (title != null || subtitle != null)) fe.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(content);
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 12),
            Child = stack
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    // 设置页：大标题、说明、内容，整体可滚动
    public static ScrollViewer Page(string title, string subtitle, UIElement body)
    {
        var heading = Label(title, 24, FontWeights.Bold);
        var detail = Label(subtitle, 12, secondary: true, wrap: true);
        detail.Margin = new Thickness(0, 8, 0, 0);
        if (body is FrameworkElement fe) fe.Margin = new Thickness(0, 24, 0, 0);
        var stack = new StackPanel { Margin = new Thickness(24, 26, 28, 26) };
        stack.Children.Add(heading);
        stack.Children.Add(detail);
        stack.Children.Add(body);
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // 表单行：右对齐的固定宽度标签 + 控件
    public static StackPanel FormRow(string title, double labelWidth, params UIElement[] controls)
    {
        var label = Label(title);
        label.Width = labelWidth;
        label.TextAlignment = TextAlignment.Right;
        var row = HStack(8, [label, .. controls]);
        row.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    // 两列均分
    public static Grid TwoColumns(UIElement left, UIElement right, double spacing = 24)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(spacing) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        System.Windows.Controls.Grid.SetColumn(left, 0);
        System.Windows.Controls.Grid.SetColumn(right, 2);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }

    // 等宽网格，不足一行用空位补齐
    public static UniformGrid Grid(IEnumerable<UIElement> items, int columns)
    {
        var g = new UniformGrid { Columns = columns };
        foreach (var item in items)
        {
            if (item is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8);
            g.Children.Add(item);
        }
        return g;
    }

    public static Button Button(string title, Action onClick, bool small = true, string? tip = null)
    {
        var b = new Button { Content = title, ToolTip = tip, Padding = small ? new Thickness(10, 3, 10, 4) : new Thickness(14, 5, 14, 6), FontSize = small ? 12 : 13 };
        b.Click += (_, _) => onClick();
        return b;
    }

    // 无边框文字按钮
    public static Button TextButton(string title, Action onClick, Brush? foreground = null)
    {
        var b = new Button
        {
            Content = title,
            FontSize = 11,
            Padding = new Thickness(4, 1, 4, 1),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        if (foreground != null) b.Foreground = foreground;
        b.Click += (_, _) => onClick();
        return b;
    }

    public static CheckBox Check(string title, bool value, Action<bool> changed, string? tip = null)
    {
        var c = new CheckBox { Content = title, IsChecked = value, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    // 分段选择：一组互斥的切换按钮
    public static StackPanel Segmented(IReadOnlyList<string> titles, int selected, Action<int> changed, string? tip = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = tip };
        var buttons = new List<ToggleButton>();
        for (var i = 0; i < titles.Count; i++)
        {
            var index = i;
            var b = new ToggleButton
            {
                Content = titles[i],
                IsChecked = i == selected,
                Padding = new Thickness(10, 3, 10, 4),
                FontSize = 12,
                MinWidth = 40,
                Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0)
            };
            b.Click += (_, _) =>
            {
                foreach (var other in buttons) other.IsChecked = other == b;
                changed(index);
            };
            buttons.Add(b);
            panel.Children.Add(b);
        }
        return panel;
    }

    public static ComboBox Combo(IEnumerable<string> titles, int selected, Action<int> changed, double minWidth = 140)
    {
        var c = new ComboBox { MinWidth = minWidth, FontSize = 12 };
        foreach (var t in titles) c.Items.Add(t);
        c.SelectedIndex = selected;
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedIndex >= 0) changed(c.SelectedIndex);
        };
        return c;
    }

    public static TextBox Field(string text = "", string placeholder = "", double width = double.NaN, bool mono = false)
    {
        var t = new TextBox { Text = text, Width = width, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        if (mono) t.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Placeholder.Set(t, placeholder);
        return t;
    }

    public static Separator Divider() => new() { Margin = new Thickness(0, 4, 0, 4) };

    // 只在内容不同时改文字，避免触发多余的 TextChanged 和光标跳动
    public static void Set(TextBox box, string text)
    {
        if (box.Text != text) box.Text = text;
    }

    // 确认框：返回是否点了主按钮
    public static bool Confirm(Window? owner, string title, string message, string confirm, string cancel = "取消", bool destructive = false)
    {
        var dialog = new SimpleDialog(title, message, confirm, cancel, null, destructive);
        if (owner != null && owner.IsVisible) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    // 带一个输入框的对话框；取消返回 null
    public static string? Prompt(Window? owner, string title, string message, string initial, string placeholder, string confirm, bool destructive = false)
    {
        var dialog = new SimpleDialog(title, message, confirm, "取消", (initial, placeholder), destructive);
        if (owner != null && owner.IsVisible) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.InputText : null;
    }

    public static void Alert(Window? owner, string title, string message)
    {
        var dialog = new SimpleDialog(title, message, "好", null, null, false);
        if (owner != null && owner.IsVisible) dialog.Owner = owner;
        dialog.ShowDialog();
    }
}

// TextBox 的占位提示
internal static class Placeholder
{
    public static void Set(TextBox box, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        box.ToolTip ??= text;
        box.Loaded += (_, _) => Attach(box, text);
    }

    private static void Attach(TextBox box, string text)
    {
        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(box);
        if (layer == null) return;
        var adorner = new PlaceholderAdorner(box, text);
        layer.Add(adorner);
        void Update() => adorner.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Update();
        Update();
    }

    private sealed class PlaceholderAdorner(TextBox box, string text) : System.Windows.Documents.Adorner(box)
    {
        protected override void OnRender(DrawingContext dc)
        {
            var brush = (box.TryFindResource("TextFillColorTertiaryBrush") as Brush) ?? Brushes.Gray;
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(box.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), box.FontSize, brush,
                VisualTreeHelper.GetDpi(box).PixelsPerDip);
            var left = box.Padding.Left + box.BorderThickness.Left + 4;
            dc.DrawText(ft, new Point(left, (box.ActualHeight - ft.Height) / 2));
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
    }
}

// 简单对话框：说明文字、可选输入框、一到两个按钮
internal sealed class SimpleDialog : Window
{
    private readonly TextBox? _input;

    public string InputText => _input?.Text.Trim() ?? "";

    public SimpleDialog(string title, string message, string confirm, string? cancel, (string Initial, string Placeholder)? input, bool destructive)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Topmost = true;
        var stack = new StackPanel { Margin = new Thickness(20, 18, 20, 16), MaxWidth = 460 };
        stack.Children.Add(UiKit.Label(title, 14, FontWeights.SemiBold, wrap: true));
        if (!string.IsNullOrEmpty(message))
        {
            var m = UiKit.Label(message, 12, wrap: true);
            m.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(m);
        }
        if (input is { } spec)
        {
            _input = UiKit.Field(spec.Initial, spec.Placeholder, 300);
            _input.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(_input);
            Loaded += (_, _) =>
            {
                _input.Focus();
                _input.SelectAll();
            };
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = confirm, IsDefault = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5) };
        if (destructive) ok.Foreground = UiKit.ErrorBrush;
        else ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        if (cancel != null)
        {
            var no = new Button { Content = cancel, IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(no);
        }
        stack.Children.Add(buttons);
        Content = stack;
    }
}
