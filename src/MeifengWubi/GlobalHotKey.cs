using System.Windows.Interop;
using Meifeng.Core.Config;
using MeifengWubi.Interop;

namespace MeifengWubi;

// 全局造词快捷键（RegisterHotKey），快捷键设置变化时重新注册。只在界面线程使用
internal sealed class GlobalHotKey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int Id = 0x4D46;

    private readonly HwndSource _source;
    private readonly Action _pressed;
    private bool _registered;

    // 注册失败（被别的软件占用）时的说明，设置页显示
    public static string? LastError { get; private set; }

    public GlobalHotKey(Action pressed)
    {
        _pressed = pressed;
        // 仅接收消息的隐藏窗口
        _source = new HwndSource(new HwndSourceParameters("MeifengWubiHotKey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _source.AddHook(Hook);
        Register();
        QuickAddPrefs.Changed += OnPrefsChanged;
    }

    private void OnPrefsChanged() => _source.Dispatcher.BeginInvoke(Register);

    private void Register()
    {
        if (_registered) Native.UnregisterHotKey(_source.Handle, Id);
        var mods = (uint)QuickAddPrefs.Modifiers | Native.MOD_NOREPEAT;
        _registered = Native.RegisterHotKey(_source.Handle, Id, mods, QuickAddPrefs.KeyCode);
        LastError = _registered ? null : $"快捷键 {QuickAddPrefs.Display} 已被其他软件占用。";
        if (!_registered) Meifeng.Core.Log.Write($"hotkey register failed {QuickAddPrefs.Display}");
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == Id)
        {
            handled = true;
            _pressed();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        QuickAddPrefs.Changed -= OnPrefsChanged;
        if (_registered) Native.UnregisterHotKey(_source.Handle, Id);
        _source.Dispose();
    }
}
