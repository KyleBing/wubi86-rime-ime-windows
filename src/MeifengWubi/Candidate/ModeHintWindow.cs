using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Meifeng.Core.Config;
using MeifengWubi.Interop;
using MeifengWubi.Ui;

namespace MeifengWubi.Candidate;

// 光标旁短暂出现的中/英模式胶囊（对应 macOS ModeHintController）。只在界面线程使用
internal sealed class ModeHintWindow : IDisposable
{
    private const double ShadowOutset = 14;
    private const double ContentHeight = 28;

    private readonly LayeredPopup _popup = new(clickThrough: true);
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _shownAt;
    private double _outsetPx;
    private int _bitmapW;
    private int _bitmapH;

    public ModeHintWindow()
    {
        _timer.Tick += (_, _) => Animate();
    }

    public bool Visible => _popup.Visible;

    // 胶囊文字：大写锁定优先于中英，锁定时一直显示「大写」
    private static string Label(bool asciiMode, bool capsLock) =>
        capsLock ? "大写" : asciiMode ? "英文" : "中文";

    // 在光标旁显示当前模式：0.12 秒淡入，约 0.9 秒后 0.28 秒淡出
    public void Show(bool asciiMode, bool capsLock, Native.RECT caret)
    {
        var (work, scale) = Native.MonitorAt(caret.Left, caret.Bottom);
        var skin = AppearancePrefs.Skin;
        var colors = skin.ColorsFor(SkinMedia.SystemIsDark);
        var root = BuildChrome(Label(asciiMode, capsLock), colors, out var size);
        var bitmap = CandidateBarRenderer.Rasterize(root, size, scale);

        _outsetPx = ShadowOutset * scale;
        _bitmapW = bitmap.PixelWidth;
        _bitmapH = bitmap.PixelHeight;
        var (x, y) = Place(caret, work, scale, skin);

        _popup.SetAlpha(0);
        _popup.Present(bitmap, x, y);
        _popup.SetAlpha(0);
        _shownAt = DateTime.UtcNow;
        _timer.Start();
    }

    // 聚焦后插入点还会跳：已显示时跟着新光标挪，不重开动效
    public void FollowCaret(Native.RECT caret)
    {
        if (!_popup.Visible || _bitmapW <= 0) return;
        var (work, scale) = Native.MonitorAt(caret.Left, caret.Bottom);
        var (x, y) = Place(caret, work, scale, AppearancePrefs.Skin);
        _popup.MoveTo(x, y);
    }

    // 开始打字、失焦或弹出候选时立刻收起
    public void Hide()
    {
        _timer.Stop();
        _popup.Hide();
    }

    public void Dispose()
    {
        _timer.Stop();
        _popup.Dispose();
    }

    private (int X, int Y) Place(Native.RECT caret, Native.RECT work, double scale, CandidateSkin skin)
    {
        var gapY = skin.OffsetTop * scale;
        var gapX = skin.OffsetLeft * scale;
        var x = caret.Left + gapX - _outsetPx;
        var y = caret.Bottom + gapY - _outsetPx;
        if (y + _bitmapH - _outsetPx > work.Bottom)
            y = Math.Max(caret.Top - gapY - (_bitmapH - _outsetPx), work.Top - _outsetPx);
        x = Math.Min(Math.Max(x, work.Left - _outsetPx), work.Right - _bitmapW + _outsetPx);
        y = Math.Max(y, work.Top - _outsetPx);
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    private void Animate()
    {
        var t = (DateTime.UtcNow - _shownAt).TotalSeconds;
        double alpha;
        if (t < 0.12) alpha = 1 - Math.Pow(1 - t / 0.12, 2);
        else if (t < 0.9) alpha = 1;
        else if (t < 1.18) alpha = 1 - Math.Pow((t - 0.9) / 0.28, 2);
        else
        {
            Hide();
            return;
        }
        _popup.SetAlpha((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255));
    }

    // 小字圆角胶囊：文字颜色跟候选词，底色跟候选窗背景
    private static FrameworkElement BuildChrome(string label, SkinColors colors, out Size size)
    {
        var text = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily(SkinMedia.DefaultFontFamily),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = colors.CandidateText.ToBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var contentWidth = Math.Ceiling(text.DesiredSize.Width) + 22;
        var capsule = new Border
        {
            Width = contentWidth,
            Height = ContentHeight,
            CornerRadius = new CornerRadius(ContentHeight / 2),
            Background = colors.Background.ToBrush(),
            Child = text,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.16, BlurRadius = 16, ShadowDepth = 2, Direction = 270 }
        };
        var root = new Canvas { Width = contentWidth + ShadowOutset * 2, Height = ContentHeight + ShadowOutset * 2 };
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        Canvas.SetLeft(capsule, ShadowOutset);
        Canvas.SetTop(capsule, ShadowOutset);
        root.Children.Add(capsule);
        size = new Size(root.Width, root.Height);
        return root;
    }
}
