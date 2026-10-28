using System.Windows.Threading;
using Meifeng.Core.Input;
using MeifengWubi.Candidate;
using MeifengWubi.Dialogs;
using MeifengWubi.Interop;

namespace MeifengWubi;

// IImeUi 的 WPF 实现：引擎线程调用，统一投递到界面线程执行，绝不阻塞调用方
internal sealed class ImeUi : IImeUi, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private CandidateWindow? _candidates;
    private ModeHintWindow? _modeHint;
    private Native.RECT _caret;
    private int _modeHintEpoch;
    private volatile bool _deleteOpen;
    private volatile bool _rankOpen;

    // 构造后再接上控制器（两者互相引用）
    public InputController? Controller { get; set; }

    public ImeUi(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _dispatcher.Invoke(() =>
        {
            _candidates = new CandidateWindow();
            _modeHint = new ModeHintWindow();
            // 点击候选在后台交给控制器，避免界面线程等引擎锁
            _candidates.CandidateClicked += index =>
            {
                var controller = Controller;
                if (controller != null) Task.Run(() => controller.ClickCandidate(index));
            };
        });
    }

    public bool IsDeletePanelOpen => _deleteOpen;
    public bool IsRankPanelOpen => _rankOpen;

    // 候选条真正显示或关掉时通知。排序热键只在显示期间登记
    public event Action<bool>? CandidatePopupChanged;

    private void Post(Action action) => _dispatcher.BeginInvoke(action);

    // 已经在界面线程（全局热键回调）时直接执行，这样窗口能在 WM_HOTKEY 里抢到前台
    private void PostOrRun(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else Post(action);
    }

    public void SetCaret(int x, int y, int width, int height)
    {
        var rect = new Native.RECT { Left = x, Top = y, Right = x + Math.Max(1, width), Bottom = y + Math.Max(1, height) };
        Post(() =>
        {
            _caret = rect;
            _candidates?.SetCaret(rect);
            // 聚焦探针还会继续上报：胶囊已显示时跟着挪到新光标
            if (_modeHint?.Visible == true) _modeHint.FollowCaret(rect);
        });
    }

    public void ShowCandidates(CandidatePage page) => Post(() =>
    {
        ++_modeHintEpoch;
        _modeHint?.Hide();
        _candidates?.SetCaret(_caret);
        _candidates?.Show(page);
        CandidatePopupChanged?.Invoke(_candidates?.Visible == true);
    });

    public void HideCandidates() => Post(() =>
    {
        _candidates?.Hide();
        CandidatePopupChanged?.Invoke(false);
    });

    public void ShowModeHint(bool asciiMode, bool capsLock) => Post(() =>
    {
        if (_candidates?.Visible == true) return;
        // 作废上一次未完成的延迟显示，避免旧光标把胶囊闪到错误位置
        var token = ++_modeHintEpoch;
        // 等 WantCaret 刷新光标（Shift / CapsLock）；聚焦路径由文本服务延迟后再报
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (token != _modeHintEpoch) return;
            if (_candidates?.Visible == true) return;
            if (_caret.Right <= _caret.Left && _caret.Bottom <= _caret.Top) return;
            _modeHint?.Show(asciiMode, capsLock, _caret);
        };
        timer.Start();
    });

    public void HideModeHint() => Post(() =>
    {
        ++_modeHintEpoch;
        _modeHint?.Hide();
    });

    // 外观设置变化后重画当前候选
    public void RefreshAppearance() => Post(() => _candidates?.Refresh());

    // 标记立即生效：窗口出现前的按键也按“面板已打开”放行
    public void PresentDeletePanel(string candidate, string preedit, Action<string, bool> onClosed)
    {
        _deleteOpen = true;
        Post(() =>
        {
            _candidates?.Hide();
            DeleteCandidateWindow.Present(candidate, preedit, Wrap(onClosed, () => _deleteOpen = false));
        });
    }

    public void PresentDeleteUnavailable(string reason, string preedit, Action<string, bool> onClosed)
    {
        _deleteOpen = true;
        Post(() =>
        {
            _candidates?.Hide();
            DeleteCandidateWindow.PresentUnavailable(reason, preedit, Wrap(onClosed, () => _deleteOpen = false));
        });
    }

    public void PresentRankPanel(string preedit, IReadOnlyList<string> candidates, Action<string, bool> onClosed)
    {
        _rankOpen = true;
        PostOrRun(() =>
        {
            _candidates?.Hide();
            CandidatePopupChanged?.Invoke(false);
            CandidateRankWindow.Present(preedit, candidates, Wrap(onClosed, () => _rankOpen = false));
        });
    }

    public void PresentRankUnavailable(string reason, string preedit, Action<string, bool> onClosed)
    {
        _rankOpen = true;
        PostOrRun(() =>
        {
            _candidates?.Hide();
            CandidatePopupChanged?.Invoke(false);
            CandidateRankWindow.PresentUnavailable(reason, preedit, Wrap(onClosed, () => _rankOpen = false));
        });
    }

    public void NoteCommittedWord(string text) => QuickAddWindow.NoteCommittedWord(text);

    // 关闭回调只执行一次：先清标记，再把编码交回控制器
    private static Action<string, bool> Wrap(Action<string, bool> onClosed, Action clear)
    {
        var done = 0;
        return (code, reset) =>
        {
            if (Interlocked.Exchange(ref done, 1) != 0) return;
            clear();
            onClosed(code, reset);
        };
    }

    public void Dispose()
    {
        _dispatcher.Invoke(() =>
        {
            _candidates?.Dispose();
            _modeHint?.Dispose();
        });
    }
}
