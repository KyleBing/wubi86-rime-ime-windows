using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meifeng.Core.Config;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 输入习惯页：四码唯一、回车清码、翻页键、二三候选、光标提示、造词与排序快捷键
internal static class KeyboardPage
{
    public static FrameworkElement Build()
    {
        // 四码唯一 / 空码清空 / 回车清码：按键时直接判断，改完立即生效，不需要重新部署
        var uniqueFourText = "输入满四码且只有一个候选时自动上屏";
        var uniqueFour = UiKit.Check("", BehaviorPrefs.AutoSelectUniqueFourCode, on => BehaviorPrefs.AutoSelectUniqueFourCode = on, uniqueFourText);
        var uniqueFourNote = UiKit.Label(uniqueFourText, 11, secondary: true, wrap: true);
        uniqueFourNote.MaxWidth = 360;

        var emptyCodeText = "打满四码仍无候选时清空编码";
        var emptyCode = UiKit.Check("", BehaviorPrefs.EmptyCodeClear, on => BehaviorPrefs.EmptyCodeClear = on, emptyCodeText);
        var emptyCodeNote = UiKit.Label(emptyCodeText, 11, secondary: true, wrap: true);
        emptyCodeNote.MaxWidth = 360;

        var enterCodeText = "编码中按回车只清空编码，不上屏字母";
        var enterCode = UiKit.Check("", BehaviorPrefs.EnterClearsCode, on => BehaviorPrefs.EnterClearsCode = on, enterCodeText);
        var enterCodeNote = UiKit.Label(enterCodeText, 11, secondary: true, wrap: true);
        enterCodeNote.MaxWidth = 360;

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

        var note = UiKit.Label("四码唯一上屏、空码清空和回车清码在按键时直接判断，改完立即生效；z 开头的拼音反查和 date / time 等预置编码不参与。翻页与选词写入 Rime 配置，下次按键时生效。造词与排序快捷键立即作用于输入法。候选出现时，用与造词相同的修饰键加数字（默认 Ctrl+Alt+1）可删除对应候选；排序快捷键（默认 Ctrl+Alt+0）由输入法接管，避免被当前软件占用。排序只改被拖动词条的权重，幅度为 5 或 10。", 11, secondary: true, wrap: true);
        note.MaxWidth = 460;
        note.HorizontalAlignment = HorizontalAlignment.Left;

        // 排序快捷键可以单独改，默认与造词同一组修饰键加 0
        var rankHotKey = new HotKeyRecorder(() => RankPrefs.Display,
            (vk, mods, display) => RankPrefs.SaveShortcut(vk, mods, display));

        var settings = UiKit.VStack(14,
            Row("四码唯一上屏", uniqueFour, uniqueFourNote),
            Row("空码清空", emptyCode, emptyCodeNote),
            Row("回车清码", enterCode, enterCodeNote),
            Row("翻页键", paging),
            Row("二三候选", second),
            Row("光标提示", caretHint, caretNote),
            Row("造词", new QuickAddHotKeyRecorder()),
            Row("排序", rankHotKey),
            note);
        return UiKit.Page("输入习惯", "设置四码唯一上屏、空码清空、回车清码、翻页、快捷选词、造词和排序快捷键。", UiKit.Card("打码与按键", null, settings));
    }

    // 宽一点以容纳「四码唯一上屏」
    private static StackPanel Row(string title, params UIElement[] controls) => UiKit.FormRow(title, 108, controls);
}

// 快捷键录制：点一下后按下新的组合键。读取显示与保存由构造时给的委托决定
internal class HotKeyRecorder : StackPanel
{
    private readonly Button _button = new() { MinWidth = 120, Padding = new Thickness(12, 4, 12, 5) };
    private readonly TextBlock _hint = UiKit.Label("需包含 Ctrl / Alt / Shift / Win 之一。Esc 取消。", 11, secondary: true);
    private readonly TextBlock _error = UiKit.Label("", 11, wrap: true);
    private readonly Func<string> _read;
    private readonly Action<uint, HotKeyModifiers, string> _write;
    private readonly Func<string?>? _errorProvider;
    private bool _recording;

    // read：当前快捷键的显示文字；write：保存新快捷键；errorProvider：注册失败时的说明
    public HotKeyRecorder(Func<string> read, Action<uint, HotKeyModifiers, string> write, Func<string?>? errorProvider = null)
    {
        _read = read;
        _write = write;
        _errorProvider = errorProvider;
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
        Loaded += (_, _) => ShowCurrent();
        ShowCurrent();
    }

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
        _write(vk, mods, QuickAddPrefs.DisplayString(mods, KeyName(key)));
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

    protected void ShowCurrent()
    {
        if (_recording) return;
        _button.Content = _read();
        _hint.Visibility = Visibility.Collapsed;
        // 稍后再读注册结果（热键在界面线程异步重新注册）
        Dispatcher.BeginInvoke(() =>
        {
            var err = _errorProvider?.Invoke();
            _error.Visibility = err == null ? Visibility.Collapsed : Visibility.Visible;
            if (err != null) UiKit.SetTone(_error, err, Tone.Attention);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}

// 造词快捷键：保存后需要重新注册全局热键，注册失败时显示说明
internal sealed class QuickAddHotKeyRecorder : HotKeyRecorder
{
    public QuickAddHotKeyRecorder()
        : base(() => QuickAddPrefs.Display,
            (vk, mods, display) => QuickAddPrefs.SaveShortcut(vk, mods, display),
            () => GlobalHotKey.LastError)
    {
        QuickAddPrefs.Changed += OnChanged;
        Unloaded += (_, _) => QuickAddPrefs.Changed -= OnChanged;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(ShowCurrent);
}
