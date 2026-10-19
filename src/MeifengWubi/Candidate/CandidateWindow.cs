using System.Windows;
using System.Windows.Media.Imaging;
using Meifeng.Core.Config;
using Meifeng.Core.Input;
using MeifengWubi.Interop;
using MeifengWubi.Ui;

namespace MeifengWubi.Candidate;

// 光标旁的候选窗（对应 macOS CandidatePanelController）。只在界面线程使用
internal sealed class CandidateWindow : IDisposable
{
    private readonly LayeredPopup _popup = new(clickThrough: false);
    private CandidatePage? _page;
    private BitmapSource? _bitmap;
    private double _outsetPx;
    private List<Int32Rect> _hitRects = [];
    private Native.RECT _caret;

    // 点击了第 index 个候选（页内序号）
    public event Action<int>? CandidateClicked;

    public CandidateWindow()
    {
        _popup.Clicked = (x, y) =>
        {
            var index = HitTest(x, y);
            if (index >= 0) CandidateClicked?.Invoke(index);
        };
        _popup.IsClickable = (x, y) => HitTest(x, y) >= 0;
    }

    public bool Visible => _popup.Visible;

    // 光标矩形（屏幕物理像素）变化时重新定位
    public void SetCaret(Native.RECT caret)
    {
        _caret = caret;
        if (_popup.Visible && _bitmap != null) Place();
    }

    public void Show(CandidatePage page)
    {
        _page = page;
        if (AppearancePrefs.DisplayMode == CandidateDisplayMode.Hidden || page.Texts.Count == 0)
        {
            Hide();
            return;
        }
        Render();
        Place();
    }

    // 外观变化后按当前页重画
    public void Refresh()
    {
        if (_popup.Visible && _page != null) Show(_page);
    }

    public void Hide()
    {
        _popup.Hide();
    }

    public void Dispose() => _popup.Dispose();

    private void Render()
    {
        if (_page == null) return;
        var (_, scale) = Native.MonitorAt(_caret.Left, _caret.Bottom);
        var skin = AppearancePrefs.Skin;
        var colors = skin.ColorsFor(SkinMedia.SystemIsDark);
        var result = CandidateBarRenderer.Build(skin, colors, _page.Texts, _page.Codes, CandidateBarOptions.Current, true, scale);
        _bitmap = CandidateBarRenderer.Rasterize(result.Root, result.Size, scale);
        _outsetPx = result.ShadowOutset * scale;
        _hitRects = result.HitRects
            .Select(r => new Int32Rect((int)(r.X * scale), (int)(r.Y * scale), (int)Math.Ceiling(r.Width * scale), (int)Math.Ceiling(r.Height * scale)))
            .ToList();
    }

    // 默认在光标下方，左边与光标对齐；下方放不下时翻到光标上方，再收进工作区
    private void Place()
    {
        if (_bitmap == null) return;
        var (work, scale) = Native.MonitorAt(_caret.Left, _caret.Bottom);
        var skin = AppearancePrefs.Skin;
        var w = _bitmap.PixelWidth;
        var h = _bitmap.PixelHeight;
        var gapY = skin.OffsetTop * scale;
        var gapX = skin.OffsetLeft * scale;
        var x = _caret.Left + gapX - _outsetPx;
        var y = _caret.Bottom + gapY - _outsetPx;
        if (y + h - _outsetPx > work.Bottom)
        {
            y = _caret.Top - gapY - (h - _outsetPx);
        }
        x = Math.Min(Math.Max(x, work.Left - _outsetPx), work.Right - w + _outsetPx);
        y = Math.Max(y, work.Top - _outsetPx);
        _popup.Present(_bitmap, (int)Math.Round(x), (int)Math.Round(y));
    }

    private int HitTest(int x, int y)
    {
        for (var i = 0; i < _hitRects.Count; i++)
        {
            var r = _hitRects[i];
            if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height) return i;
        }
        return -1;
    }
}
