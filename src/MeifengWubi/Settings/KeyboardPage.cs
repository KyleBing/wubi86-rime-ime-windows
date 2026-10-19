using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meifeng.Core.Config;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 按键页：翻页键、二三候选、光标提示、造词快捷键
internal static class KeyboardPage
{
    public static FrameworkElement Build()
    {
        var pagingChoices = Enum.GetValues<PagingKeys>();
        var paging = UiKit.Combo(pagingChoices.Select(p => p.Title()), Array.IndexOf(pagingChoices, BehaviorPrefs.PagingKeys),
            i => BehaviorPrefs.PagingKeys = pagingChoices[i], 210);
        var secondChoices = Enum.GetValues<SecondThirdKeys>();
        var second = UiKit.Combo(secondChoices.Select(s => s.Title()), Array.IndexOf(secondChoices, BehaviorPrefs.SecondThirdKeys),
            i => BehaviorPrefs.SecondThirdKeys = secondChoices[i], 210);

        const string caretText = "输入框出现光标时，在旁边显示当前是中文还是英文";
        var caretHint = UiKit.Check("", AppearancePrefs.ShowCaretModeHint, on => AppearancePrefs.ShowCaretModeHint = on, caretText);
        var caretNote = UiKit.Label(caretText, 11, secondary: true, wrap: true);
        caretNote.MaxWidth = 360;

        var note = UiKit.Label("翻页与选词写入 Rime 配置，下次按键时生效。造词快捷键立即作用于输入法。候选出现时，用与造词相同的修饰键加数字（默认 Ctrl+Alt+1）可删除对应候选，加 0（默认 Ctrl+Alt+0）可拖动排序。这段时间输入法会接管这组键，避免被当前软件占用。排序只改被拖动词条的权重，幅度为 5 或 10。", 11, secondary: true, wrap: true);
        note.MaxWidth = 460;
        note.HorizontalAlignment = HorizontalAlignment.Left;

        var settings = UiKit.VStack(14,
            Row("翻页键", paging),
            Row("二三候选", second),
            Row("光标提示", caretHint, caretNote),
            Row("造词", new HotKeyRecorder()),
            note);
        return UiKit.Page("按键", "设置翻页、快捷选词和全局造词快捷键。", UiKit.Card("候选按键", null, settings));
    }

    private static StackPanel Row(string title, params UIElement[] controls) => UiKit.FormRow(title, 82, controls);
}

// 造词快捷键：点一下后按下新的组合键
internal sealed class HotKeyRecorder : StackPanel
{
    private readonly Button _button = new() { MinWidth = 120, Padding = new Thickness(12, 4, 12, 5) };
    private readonly TextBlock _hint = UiKit.Label("需包含 Ctrl / Alt / Shift / Win 之一。Esc 取消。", 11, secondary: true);
    private readonly TextBlock _error = UiKit.Label("", 11, wrap: true);
    private bool _recording;

    public HotKeyRecorder()
    {
        Orientation = Orientation.Vertical;
        var row = UiKit.HStack(8, _button, _hint);
        Children.Add(row);
        Children.Add(_error);
        _button.Click += (_, _) => Begin();
        _button.PreviewKeyDown += OnKey;
        _button.LostKeyboardFocus += (_, _) =>
        {
            if (_recording) Stop();
        };
        QuickAddPrefs.Changed += OnChanged;
        Unloaded += (_, _) => QuickAddPrefs.Changed -= OnChanged;
        Loaded += (_, _) => ShowCurrent();
        ShowCurrent();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(ShowCurrent);

    private void Begin()
    {
        _recording = true;
        _button.Content = "请按下快捷键…";
        _hint.Visibility = Visibility.Visible;
        _button.Focus();
    }

    private void Stop()
    {
        _recording = false;
        ShowCurrent();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Stop();
            return;
        }
        // 只按了修饰键时继续等待
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed) return;
        var mods = HotKeyModifiers.None;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= HotKeyModifiers.Control;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= HotKeyModifiers.Alt;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= HotKeyModifiers.Shift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= HotKeyModifiers.Win;
        if (mods == HotKeyModifiers.None) return;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        QuickAddPrefs.SaveShortcut(vk, mods, QuickAddPrefs.DisplayString(mods, KeyName(key)));
        _recording = false;
        ShowCurrent();
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        _ => key.ToString()
    };

    private void ShowCurrent()
    {
        if (_recording) return;
        _button.Content = QuickAddPrefs.Display;
        _hint.Visibility = Visibility.Collapsed;
        // 稍后再读注册结果（热键在界面线程异步重新注册）
        Dispatcher.BeginInvoke(() =>
        {
            var err = GlobalHotKey.LastError;
            _error.Visibility = err == null ? Visibility.Collapsed : Visibility.Visible;
            if (err != null) UiKit.SetTone(_error, err, Tone.Attention);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
