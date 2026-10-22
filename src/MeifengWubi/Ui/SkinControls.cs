using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MeifengWubi.Interop;

namespace MeifengWubi.Ui;

// 数值输入：回车或失焦提交，上下方向键步进；− / + 按钮步进。范围与 CandidateSkin.Normalized 一致
internal sealed class NumberStepper : StackPanel
{
    private readonly TextBox _box;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly double _min, _max, _step;

    public NumberStepper(Func<double> get, Action<double> set, double min, double max, double step, bool compact, string? caption = null)
    {
        _get = get;
        _set = set;
        _min = min;
        _max = max;
        _step = step;
        _box = new TextBox
        {
            Width = compact ? 40 : 46,
            FontSize = compact ? 11 : 12,
            TextAlignment = TextAlignment.Right,
            Padding = new Thickness(2, 1, 4, 1),
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
        };
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
            {
                var current = double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : _get();
                Apply(current + (e.Key == Key.Up ? _step : -_step));
                _box.SelectAll();
                e.Handled = true;
            }
        };
        _box.LostKeyboardFocus += (_, _) => Commit();

        if (compact)
        {
            // 示意图里的紧凑控件：说明文字在上，输入框 + 上下箭头在下
            Orientation = Orientation.Vertical;
            HorizontalAlignment = HorizontalAlignment.Center;
            if (caption != null)
            {
                var label = UiKit.Label(caption, 10, FontWeights.Medium, secondary: true);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Children.Add(label);
            }
            var arrows = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(1, 0, 0, 0) };
            arrows.Children.Add(Arrow("▲", 1));
            arrows.Children.Add(Arrow("▼", -1));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(_box);
            row.Children.Add(arrows);
            Children.Add(row);
        }
        else
        {
            Orientation = Orientation.Horizontal;
            Children.Add(StepButton("−", -1));
            _box.Margin = new Thickness(2, 0, 2, 0);
            Children.Add(_box);
            Children.Add(StepButton("+", 1));
        }
        Sync();
    }

    private Button StepButton(string title, int direction)
    {
        var b = new Button { Content = title, Width = 24, Height = 24, Padding = new Thickness(0), FontSize = 12, ToolTip = direction > 0 ? "增加" : "减少" };
        b.Click += (_, _) => Apply(_get() + direction * _step);
        return b;
    }

    private RepeatButton Arrow(string glyph, int direction)
    {
        var b = new RepeatButton { Content = glyph, Width = 16, Height = 11, Padding = new Thickness(0), FontSize = 6, Interval = 120 };
        b.Click += (_, _) => Apply(_get() + direction * _step);
        return b;
    }

    private void Commit()
    {
        if (double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) Apply(v);
        else Sync();
    }

    private void Apply(double value)
    {
        _set(Math.Clamp(value, _min, _max));
        Sync();
    }

    // 按已保存（已收敛）的值刷新输入框
    public void Sync()
    {
        var v = _get();
        _box.Text = Math.Abs(v - Math.Round(v)) < 1e-9 ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}

// 颜色色块：半透明时底部显示棋盘格；点击弹出取色窗，拖动即时生效
internal sealed class ColorSwatch : Border
{
    private readonly Rectangle _fill = new();
    private Color _color;
    private readonly Action<Color> _changed;

    public ColorSwatch(Color color, Action<Color> changed, string title)
    {
        _color = color;
        _changed = changed;
        Height = 22;
        MinWidth = 36;
        CornerRadius = new CornerRadius(5);
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        Cursor = Cursors.Hand;
        ToolTip = "点击选择颜色";
        ClipToBounds = true;
        var grid = new Grid();
        grid.Children.Add(new Rectangle { Fill = Checkerboard(), RadiusX = 4, RadiusY = 4 });
        _fill.RadiusX = 4;
        _fill.RadiusY = 4;
        grid.Children.Add(_fill);
        Child = grid;
        UpdateFill();
        MouseLeftButtonUp += (_, _) => ColorPickerWindow.Open(this, title, _color, c =>
        {
            _color = c;
            UpdateFill();
            _changed(c);
        });
    }

    // 编辑中高亮描边
    public void SetEditing(bool editing)
    {
        BorderThickness = new Thickness(editing ? 2 : 1);
        if (editing) SetResourceReference(BorderBrushProperty, "AccentFillColorDefaultBrush");
        else SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
    }

    private void UpdateFill() => _fill.Fill = new SolidColorBrush(_color);

    private static DrawingBrush Checkerboard()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        var gray = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(0, 0, 5, 5))));
        group.Children.Add(new GeometryDrawing(gray, null, new RectangleGeometry(new Rect(5, 5, 5, 5))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute };
    }
}

// 渐变条滑块：轨道本身就是颜色渐变，点击或拖动直接调整，值范围 0–1
internal sealed class GradientSlider : Grid
{
    private const double TrackHeight = 14;
    private const double ThumbSize = 16;
    private readonly Border _track;
    private readonly Ellipse _thumb;
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private double _value;
    private bool _dragging;

    public GradientSlider()
    {
        Height = 24;
        // 透明背景也要能接收整个滑条的点击
        Background = Brushes.Transparent;
        _track = new Border
        {
            Height = TrackHeight,
            CornerRadius = new CornerRadius(TrackHeight / 2),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        _track.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        Children.Add(_track);
        Children.Add(_canvas);
        _thumb = new Ellipse
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_thumb);
        SizeChanged += (_, _) => PlaceThumb();
        MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            CaptureMouse();
            SetFromPointer(e.GetPosition(this));
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (_dragging) SetFromPointer(e.GetPosition(this));
        };
        MouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => _dragging = false;
        PlaceThumb();
    }

    // 值变化时触发，参数为 0–1 的新值
    public event Action<double>? ValueChanged;

    public double Value => _value;

    public void SetBrush(Brush brush) => _track.Background = brush;

    public void SetValue(double value, bool notify)
    {
        _value = Math.Clamp(value, 0, 1);
        PlaceThumb();
        if (notify) ValueChanged?.Invoke(_value);
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void SetFromPointer(Point p)
    {
        SetValue(p.X / Math.Max(ActualWidth, 1), notify: true);
    }

    // 滑块圆点跟随当前值，两端各让出半个圆点
    private void PlaceThumb()
    {
        var usable = Math.Max(ActualWidth - ThumbSize, 1);
        Canvas.SetLeft(_thumb, _value * usable);
        Canvas.SetTop(_thumb, (ActualHeight - ThumbSize) / 2);
    }
}

// 取色窗：色相 / 饱和度 / 亮度 / 不透明度用渐变条调整，可输入十六进制或直接从屏幕拾取，同一时间只开一个
internal sealed class ColorPickerWindow : Window
{
    private static ColorPickerWindow? _current;

    private readonly GradientSlider _hue = new(), _sat = new(), _val = new(), _alpha = new();
    private readonly TextBox _hex = new() { Width = 96, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
    private readonly Rectangle _preview = new() { Height = 28, RadiusX = 5, RadiusY = 5 };
    private ColorSwatch _owner;
    private Action<Color> _changed;
    private bool _syncing;

    public static void Open(ColorSwatch owner, string title, Color color, Action<Color> changed)
    {
        if (_current == null)
        {
            _current = new ColorPickerWindow(owner, color, changed);
            _current.Closed += (_, _) =>
            {
                _current?._owner.SetEditing(false);
                _current = null;
            };
            _current.Owner = Window.GetWindow(owner);
        }
        else
        {
            _current._owner.SetEditing(false);
            _current._owner = owner;
            _current._changed = changed;
            _current.Load(color);
        }
        _current.Title = $"选择颜色 · {title}";
        owner.SetEditing(true);
        _current.Show();
        _current.Activate();
    }

    private ColorPickerWindow(ColorSwatch owner, Color color, Action<Color> changed)
    {
        _owner = owner;
        _changed = changed;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_preview);
        foreach (var slider in new[] { _hue, _sat, _val, _alpha })
        {
            slider.Width = 200;
            slider.ValueChanged += _ => FromSliders();
        }
        _hue.SetBrush(HueBrush());
        stack.Children.Add(Row("色相", _hue));
        stack.Children.Add(Row("饱和度", _sat));
        stack.Children.Add(Row("亮度", _val));
        stack.Children.Add(Row("不透明度", _alpha));
        // 屏幕取色：打开铺满屏幕的透明浮层，单击任意位置取色
        var pickHint = UiKit.Label("移动鼠标预览，单击取色，Esc 或右键取消", 11, secondary: true, wrap: true);
        pickHint.MaxWidth = 180;
        var pickRow = UiKit.HStack(8, UiKit.Button("拾取屏幕颜色", PickFromScreen, small: false, tip: "从屏幕任意位置取色"), pickHint);
        pickRow.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(pickRow);
        var hexRow = UiKit.FormRow("十六进制", 56, _hex, UiKit.Label("#AARRGGBB 或 #RRGGBB", 11, secondary: true));
        hexRow.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(hexRow);
        Content = stack;
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) FromHex();
        };
        _hex.LostKeyboardFocus += (_, _) => FromHex();
        Load(color);
    }

    private static StackPanel Row(string title, GradientSlider slider)
    {
        var row = UiKit.FormRow(title, 56, slider);
        row.Margin = new Thickness(0, 8, 0, 0);
        return row;
    }

    private void Load(Color c)
    {
        _syncing = true;
        var (h, s, v) = ToHsv(c);
        _hue.SetValue(h / 360.0, notify: false);
        _sat.SetValue(s, notify: false);
        _val.SetValue(v, notify: false);
        _alpha.SetValue(c.A / 255.0, notify: false);
        _syncing = false;
        RefreshBrushes();
        Show(c, notify: false);
    }

    private void FromSliders()
    {
        if (_syncing) return;
        var c = FromHsv(_hue.Value * 360, _sat.Value, _val.Value);
        c.A = (byte)Math.Round(_alpha.Value * 255);
        RefreshBrushes();
        Show(c, notify: true);
    }

    private void FromHex()
    {
        var text = _hex.Text.Trim().TrimStart('#');
        if (text.Length == 6) text = "FF" + text;
        if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return;
        var c = Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        Load(c);
        _changed(c);
    }

    // 屏幕取色返回后直接套用到当前选中的颜色块
    private void PickFromScreen()
    {
        var picker = new ScreenColorPickerWindow(ApplyPicked) { Owner = this };
        picker.ShowDialog();
        Activate();
    }

    private void ApplyPicked(Color c)
    {
        Load(c);
        _changed(c);
    }

    // 三条轨道随当前色变化：饱和度、亮度、不透明度都用真实渐变预览
    private void RefreshBrushes()
    {
        var h = _hue.Value * 360;
        var s = _sat.Value;
        var v = _val.Value;
        _sat.SetBrush(Gradient(FromHsv(h, 0, v), FromHsv(h, 1, v)));
        _val.SetBrush(Gradient(Colors.Black, FromHsv(h, s, 1)));
        var opaque = FromHsv(h, s, v);
        _alpha.SetBrush(Gradient(Color.FromArgb(0, opaque.R, opaque.G, opaque.B), opaque));
    }

    private static LinearGradientBrush Gradient(Color from, Color to) =>
        new(from, to, new Point(0, 0.5), new Point(1, 0.5));

    // 色相轨道：红 → 黄 → 绿 → 青 → 蓝 → 品红 → 红
    private static Brush HueBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (var i = 0; i <= 6; i++) brush.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
        brush.Freeze();
        return brush;
    }

    private void Show(Color c, bool notify)
    {
        _preview.Fill = new SolidColorBrush(c);
        _hex.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        if (notify) _changed(c);
    }

    private static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        static byte B(double t) => (byte)Math.Round(Math.Clamp(t, 0, 1) * 255);
        return Color.FromRgb(B(r + m), B(g + m), B(b + m));
    }
}

// 屏幕取色浮层：铺满所有显示器，实时预览光标下的颜色，单击取色；Esc 或右键取消
internal sealed class ScreenColorPickerWindow : Window
{
    private readonly Action<Color> _picked;
    private readonly Border _panel;
    private readonly Rectangle _swatch = new() { Width = 32, Height = 32, RadiusX = 4, RadiusY = 4 };
    private readonly TextBlock _hex = new()
    {
        FontSize = 11,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center
    };
    private Color _current;
    private bool _done;

    public ScreenColorPickerWindow(Action<Color> picked)
    {
        _picked = picked;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // 覆盖整个虚拟桌面，多显示器也只需要点一下
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Cursor = Cursors.Cross;

        _panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            IsHitTestVisible = false,
            Child = UiKit.HStack(8, _swatch, _hex)
        };
        var canvas = new Canvas { Background = Brushes.Transparent };
        canvas.Children.Add(_panel);
        Content = canvas;
        MouseMove += (_, e) => UpdateAt(e.GetPosition(this));
        MouseLeftButtonDown += (_, e) =>
        {
            Pick();
            e.Handled = true;
        };
        MouseRightButtonDown += (_, e) =>
        {
            Cancel();
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Cancel();
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            UpdateAt(Mouse.GetPosition(this));
        };
    }

    // 读取光标下的屏幕像素并更新跟随预览
    private void UpdateAt(Point p)
    {
        if (!Native.GetCursorPos(out var pt)) return;
        if (Native.ScreenColorAt(pt.X, pt.Y) is not { } c) return;
        _current = c;
        _swatch.Fill = new SolidColorBrush(c);
        _hex.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        _panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _panel.DesiredSize;
        var left = p.X + 18;
        var top = p.Y + 18;
        // 贴近屏幕右 / 下边缘时翻到光标另一侧
        if (left + size.Width > ActualWidth) left = p.X - size.Width - 18;
        if (top + size.Height > ActualHeight) top = p.Y - size.Height - 18;
        Canvas.SetLeft(_panel, Math.Max(0, left));
        Canvas.SetTop(_panel, Math.Max(0, top));
    }

    private void Pick()
    {
        if (_done) return;
        _done = true;
        _picked(_current);
        Close();
    }

    private void Cancel()
    {
        _done = true;
        Close();
    }
}
