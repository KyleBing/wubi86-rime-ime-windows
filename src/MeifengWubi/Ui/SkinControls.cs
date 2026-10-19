using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

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

// 取色窗：色相 / 饱和度 / 亮度 / 不透明度滑块和十六进制输入，同一时间只开一个
internal sealed class ColorPickerWindow : Window
{
    private static ColorPickerWindow? _current;

    private readonly Slider _h = Make(0, 360), _s = Make(0, 100), _v = Make(0, 100), _a = Make(0, 100);
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
        Width = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_preview);
        stack.Children.Add(Row("色相", _h));
        stack.Children.Add(Row("饱和度", _s));
        stack.Children.Add(Row("亮度", _v));
        stack.Children.Add(Row("不透明度", _a));
        var hexRow = UiKit.FormRow("十六进制", 56, _hex, UiKit.Label("#AARRGGBB 或 #RRGGBB", 11, secondary: true));
        hexRow.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(hexRow);
        Content = stack;
        foreach (var s in new[] { _h, _s, _v, _a }) s.ValueChanged += (_, _) => FromSliders();
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) FromHex();
        };
        _hex.LostKeyboardFocus += (_, _) => FromHex();
        Load(color);
    }

    private static Slider Make(double min, double max) => new() { Minimum = min, Maximum = max, Width = 200, VerticalAlignment = VerticalAlignment.Center };

    private static StackPanel Row(string title, Slider slider)
    {
        var row = UiKit.FormRow(title, 56, slider);
        row.Margin = new Thickness(0, 8, 0, 0);
        return row;
    }

    private void Load(Color c)
    {
        _syncing = true;
        var (h, s, v) = ToHsv(c);
        _h.Value = h;
        _s.Value = s * 100;
        _v.Value = v * 100;
        _a.Value = c.A / 255.0 * 100;
        _syncing = false;
        Show(c, notify: false);
    }

    private void FromSliders()
    {
        if (_syncing) return;
        var c = FromHsv(_h.Value, _s.Value / 100, _v.Value / 100);
        c.A = (byte)Math.Round(_a.Value / 100 * 255);
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
