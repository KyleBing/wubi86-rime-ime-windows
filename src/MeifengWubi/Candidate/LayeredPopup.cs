using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using MeifengWubi.Interop;
using static MeifengWubi.Interop.Native;

namespace MeifengWubi.Candidate;

// 不抢焦点的置顶分层窗口。内容为预乘 Alpha 的位图，经 UpdateLayeredWindow 贴到屏幕
// 必须在界面线程创建和使用
internal sealed class LayeredPopup : IDisposable
{
    private static readonly WndProc ProcDelegate = StaticWndProc;
    private static readonly Dictionary<nint, LayeredPopup> Instances = new();
    private static bool _classRegistered;
    private const string ClassName = "MeifengWubiPopup";

    private readonly nint _hwnd;
    private int _x, _y, _width, _height;
    private byte _alpha = 255;
    private nint _hdcMem, _bitmap;

    // 点击回调：参数为窗口内物理像素坐标
    public Action<int, int>? Clicked;

    // 鼠标所在位置是否可点（显示手型）
    public Func<int, int, bool>? IsClickable;

    public bool Visible { get; private set; }

    public LayeredPopup(bool clickThrough)
    {
        EnsureClass();
        var ex = WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (clickThrough) ex |= WS_EX_TRANSPARENT;
        _hwnd = CreateWindowExW(ex, ClassName, "", WS_POPUP, 0, 0, 1, 1, 0, 0, GetModuleHandleW(0), 0);
        Instances[_hwnd] = this;
    }

    public nint Handle => _hwnd;

    // 显示位图，左上角放在屏幕物理坐标 (x, y)
    public void Present(BitmapSource bitmap, int x, int y)
    {
        _x = x;
        _y = y;
        UploadBitmap(bitmap);
        Apply();
        if (!Visible)
        {
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            Visible = true;
        }
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // 整体透明度（淡入淡出用）
    public void SetAlpha(byte alpha)
    {
        _alpha = alpha;
        if (_hdcMem == 0) return;
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindowAlpha(_hwnd, 0, 0, 0, 0, 0, 0, ref blend, ULW_ALPHA);
    }

    // 只挪位置，不重传位图（模式提示跟光标用）
    public void MoveTo(int x, int y)
    {
        if (!Visible || _hdcMem == 0) return;
        _x = x;
        _y = y;
        Apply();
    }

    public void Hide()
    {
        if (!Visible) return;
        ShowWindow(_hwnd, SW_HIDE);
        Visible = false;
    }

    public void Dispose()
    {
        FreeBitmap();
        Instances.Remove(_hwnd);
        DestroyWindow(_hwnd);
    }

    // 把 WPF 位图复制进 DIB section（Pbgra32 本身就是预乘 Alpha）
    private void UploadBitmap(BitmapSource bitmap)
    {
        FreeBitmap();
        _width = bitmap.PixelWidth;
        _height = bitmap.PixelHeight;
        var screen = GetDC(0);
        _hdcMem = CreateCompatibleDC(screen);
        ReleaseDC(0, screen);
        var bmi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = _width,
            biHeight = -_height,
            biPlanes = 1,
            biBitCount = 32
        };
        _bitmap = CreateDIBSection(_hdcMem, ref bmi, 0, out var bits, 0, 0);
        bitmap.CopyPixels(System.Windows.Int32Rect.Empty, bits, _width * _height * 4, _width * 4);
        SelectObject(_hdcMem, _bitmap);
    }

    private void Apply()
    {
        var dst = new POINT(_x, _y);
        var size = new SIZE(_width, _height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = _alpha, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(_hwnd, 0, ref dst, ref size, _hdcMem, ref src, 0, ref blend, ULW_ALPHA);
    }

    private void FreeBitmap()
    {
        if (_hdcMem != 0) DeleteDC(_hdcMem);
        if (_bitmap != 0) DeleteObject(_bitmap);
        _hdcMem = 0;
        _bitmap = 0;
    }

    private static void EnsureClass()
    {
        if (_classRegistered) return;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ProcDelegate),
            hInstance = GetModuleHandleW(0),
            hCursor = LoadCursorW(0, IDC_ARROW),
            lpszClassName = ClassName
        };
        RegisterClassExW(ref wc);
        _classRegistered = true;
    }

    private static nint StaticWndProc(nint hwnd, int msg, nint wParam, nint lParam)
    {
        Instances.TryGetValue(hwnd, out var self);
        switch (msg)
        {
            // 点击候选不能激活本进程，否则原软件失焦、编码被清掉
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_SETCURSOR when self != null:
            {
                GetCursorPos(out var p);
                var hand = self.IsClickable?.Invoke(p.X - self._x, p.Y - self._y) == true;
                SetCursor(LoadCursorW(0, hand ? IDC_HAND : IDC_ARROW));
                return 1;
            }
            case WM_LBUTTONUP when self != null:
            {
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                self.Clicked?.Invoke(x, y);
                return 0;
            }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);
}
