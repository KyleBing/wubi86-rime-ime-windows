using System.Windows.Interop;
using Meifeng.Core.Config;
using Meifeng.Core.Input;
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

// 候选条显示期间，把排序快捷键（默认 Ctrl+Alt+0，可在「输入习惯」页单独改）登记成全局热键。
// 前台软件（编辑器、浏览器）常常自己吃掉这组键，文本服务收不到；登记之后按键会先到这里。
// 候选条关掉就注销，避免平时占住这组键。收到 WM_HOTKEY 的线程允许把窗口拉到前台。
internal sealed class RankHotKey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int IdMain = 0x4D30;
    private const int IdNumpad = 0x4D60;

    private readonly HwndSource _source;
    private readonly Action _pressed;
    private bool _armed;
    private bool _main;
    private bool _numpad;

    public RankHotKey(Action pressed)
    {
        _pressed = pressed;
        _source = new HwndSource(new HwndSourceParameters("MeifengWubiRankHotKey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _source.AddHook(Hook);
        // 排序键自己改了要重登记；造词键改成同一组时也要让出
        RankPrefs.Changed += OnPrefsChanged;
        QuickAddPrefs.Changed += OnPrefsChanged;
    }

    // 候选条出现时登记，消失时注销。已经是目标状态就不动，避免每次翻页都重注册
    public void SetArmed(bool armed)
    {
        if (_armed == armed && (!armed || _main || _numpad)) return;
        _armed = armed;
        Apply();
    }

    private void OnPrefsChanged() => _source.Dispatcher.BeginInvoke(Apply);

    private void Apply()
    {
        if (_main) Native.UnregisterHotKey(_source.Handle, IdMain);
        if (_numpad) Native.UnregisterHotKey(_source.Handle, IdNumpad);
        _main = false;
        _numpad = false;
        if (!_armed || RankPrefs.Modifiers == HotKeyModifiers.None) return;
        var (vk, mods) = (RankPrefs.KeyCode, RankPrefs.Modifiers);
        // 排序键与造词键撞在一起时不再登记，交给造词那个全局热键
        if (mods == QuickAddPrefs.Modifiers && vk == QuickAddPrefs.KeyCode) return;
        var nativeMods = (uint)mods | Native.MOD_NOREPEAT;
        _main = TryRegister(IdMain, vk, nativeMods);
        // 排序键是主键盘的 0 时，数字键盘的 0 也一起接管
        if (vk == Vk.D0) _numpad = TryRegister(IdNumpad, (uint)Vk.Numpad0, nativeMods);
    }

    // 登记失败只记日志，不影响按键原本的路径
    private bool TryRegister(int id, uint vk, uint mods)
    {
        var ok = Native.RegisterHotKey(_source.Handle, id, mods, vk);
        if (!ok) Meifeng.Core.Log.Write($"rank hotkey register failed vk={vk}");
        return ok;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var id = wParam.ToInt32();
        if (msg == WmHotKey && (id == IdMain || id == IdNumpad))
        {
            handled = true;
            _pressed();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        RankPrefs.Changed -= OnPrefsChanged;
        QuickAddPrefs.Changed -= OnPrefsChanged;
        if (_main) Native.UnregisterHotKey(_source.Handle, IdMain);
        if (_numpad) Native.UnregisterHotKey(_source.Handle, IdNumpad);
        _source.Dispose();
    }
}
