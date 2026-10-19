using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Meifeng.Core.Config;
using MeifengWubi.Interop;

namespace MeifengWubi.Dialogs;

// 输入法弹出的小窗口：置顶、居中、标题栏用品牌图标
internal abstract class ImeDialog : Window
{
    protected ImeDialog(string title, double width)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = true;
        Icon = BrandIcon;
    }

    // 标题栏和任务栏用的枫叶图标
    public static BitmapFrame? BrandIcon
    {
        get
        {
            try
            {
                var path = Path.Combine(AppPaths.ResourcesDir, "Brand", "AppIcon.ico");
                return File.Exists(path) ? BitmapFrame.Create(new Uri(path)) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    // 显示并抢到前台。全局热键或文本服务的 AllowSetForegroundWindow 允许这次置顶
    public void ShowAndActivate()
    {
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) Native.SetForegroundWindow(hwnd);
        Activate();
        Focus();
    }
}
