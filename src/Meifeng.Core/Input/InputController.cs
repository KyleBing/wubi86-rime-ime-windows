using System.Text.RegularExpressions;
using Meifeng.Core.Config;
using Meifeng.Core.Engine;
using Meifeng.Core.Lexicon;

namespace Meifeng.Core.Input;

// 玫枫五笔按键处理（对应 macOS 版 MeifengInputController）
// 所有输入框共用一个控制器和一个 Rime 会话；文本服务把按键送来，这里返回是否吃掉以及上屏 / 编码变化
public sealed class InputController
{
    private readonly RimeEngine _engine;
    private readonly IImeUi _ui;
    private readonly object _lock;

    // 中英状态全局共享
    private bool _asciiMode;
    private bool _shiftIsSticky;
    private bool _sawOtherKeyWithShift;
    // 大写锁定；模式提示里大写优先于中英
    private bool _capsLock;

    // 预置候选（date / time）由输入法自行分页，记录当前页与所属编码
    private int _specialPage;
    private string _specialPreedit = "";

    // 输入法自己的标点候选，不交给 Rime
    private string[] _punctSymbols = [];
    private int _punctPage;

    // 数字后的半角标点只看上一次输入，不看光标前已经有的文字
    private enum RecentKind { Other, Digit, HalfWidthAfterDigit }
    private RecentKind _recent = RecentKind.Other;
    private string _recentHalfWidth = "";
    // 中文引号、单引号：第一次前引号，第二次后引号
    private readonly PunctuationPrefs.PairState _quotePair = new();

    // 当前编码区文字（marked text）
    private string _marked = "";

    // 当前候选页点击时对应的动作
    private Func<int, bool>? _onCandidateClick;

    // 当前聚焦的文本服务实例与它的待取输出
    private long _activeClient;
    private readonly Dictionary<long, ImeOutput> _pending = new();

    // 删除 / 排序窗口打开时原来的输入框；窗口关闭后把编码交回它
    private long _panelClient;
    private bool _panelPending;

    private ImeOutput _out = new();

    private static readonly Regex ReverseLookup = new(@"^z[a-z]*'?$");

    // 有待取输出时触发（参数为文本服务实例号），宿主据此通知对应窗口
    public event Action<long>? OutputPending;

    // 中英状态变化时触发，更新语言栏图标
    public event Action<bool>? AsciiModeChanged;

    public InputController(RimeEngine engine, IImeUi ui)
    {
        _engine = engine;
        _ui = ui;
        _lock = engine.Lock;
    }

    // 与 Rime 引擎共用的锁；管道服务用它做限时等待
    public object SyncRoot => _lock;

    public bool AsciiMode
    {
        get { lock (_lock) return _asciiMode; }
    }

    public long ActiveClient
    {
        get { lock (_lock) return _activeClient; }
    }

    // ---- 文本服务入口 ----

    // 输入框获得焦点（activateServer）；返回是否需要回报光标显示模式提示
    public ImeOutput Focus(long client)
    {
        lock (_lock)
        {
            SwitchClient(client);
            _out = new ImeOutput();
            _engine.EnsureStarted();
            // 光标出现时先标出当前中/英，避免打字后才发现要切换
            if (AppearancePrefs.ShowCaretModeHint) _out.WantCaret = true;
            Log.Write($"focus client={client} rime={_engine.Started}");
            return TakeOutput();
        }
    }

    // 输入框失去焦点（deactivateServer）：提交或清掉未上屏内容
    public ImeOutput Blur(long client)
    {
        lock (_lock)
        {
            _out = new ImeOutput();
            if (client != _activeClient && _activeClient != 0) return TakeOutput();
            if (_panelPending && client == _panelClient)
            {
                // 删除 / 排序窗口抢走焦点：编码留着，窗口关掉后再显示候选
                _ui.HideCandidates();
                _ui.HideModeHint();
                _out.Preedit = _marked;
                return TakeOutput();
            }
            Deactivate();
            if (_activeClient == client) _activeClient = 0;
            return TakeOutput();
        }
    }

    // 切换当前输入框。新输入框的聚焦可能先于旧输入框的失焦到达：先替旧输入框收尾，编码不能带过去
    private void SwitchClient(long client)
    {
        var previous = _activeClient;
        if (previous != 0 && previous != client && !(_panelPending && previous == _panelClient))
        {
            _out = new ImeOutput();
            Deactivate();
            QueueOut(previous);
        }
        _activeClient = client;
    }

    // 失焦收尾：提交待上屏文字、清掉编码与候选
    private void Deactivate()
    {
        CommitOrClearPending(false);
        ClearPunctuation();
        _recent = RecentKind.Other;
        _quotePair.Reset();
        _ui.HideCandidates();
        // 输入框失焦后提示不再属于当前会话，立刻收掉
        _ui.HideModeHint();
        ClearMarkedText();
        _specialPreedit = "";
        _specialPage = 0;
    }

    // 应用把编码区结束了（点到别处等）。文字已经留在文档里，这里只清掉 Rime 编码
    public void CompositionTerminated(long client)
    {
        lock (_lock)
        {
            if (client != _activeClient) return;
            if (_engine.Started) _engine.ClearComposition();
            ClearPunctuation();
            _ui.HideCandidates();
            _marked = "";
            _specialPreedit = "";
            _specialPage = 0;
        }
    }

    // 处理一次按键（含抬起）。eaten 为 true 表示这颗键由输入法处理，不再交给应用
    public (bool Eaten, ImeOutput Output) HandleKey(long client, KeyEvent key)
    {
        lock (_lock)
        {
            if (_activeClient != client) SwitchClient(client);
            _out = new ImeOutput { Preedit = _marked };
            // 大写锁定变化：按中英提示同样的样式显示，锁定时优先显示「大写」
            if (!key.IsUp) TrackCapsLock(key);
            var passToClient = false;
            var eaten = HandleKeyCore(key, ref passToClient);
            // 按键最终交给当前软件时记下，用来判断下一次标点是不是紧跟数字
            if (passToClient && !key.IsUp) NoteKeyPassedToClient(key);
            // 这次按键若结束了编码，再加载自动同步写回的用户词
            if (!key.IsUp) FlushReloadIfIdle();
            return (eaten, TakeOutput());
        }
    }

    // 取走某个文本服务的待取输出
    public ImeOutput? FetchPending(long client)
    {
        lock (_lock)
        {
            if (!_pending.Remove(client, out var o)) return null;
            o.Preedit = client == _activeClient || client == _panelClient ? _marked : o.Preedit;
            return o;
        }
    }

    // 点击候选窗第 index 项（当前页内序号）
    public void ClickCandidate(int index)
    {
        lock (_lock)
        {
            if (_onCandidateClick == null) return;
            _out = new ImeOutput { Preedit = _marked };
            _onCandidateClick(index);
            QueueOut(_activeClient);
        }
    }

    // 语言栏 / 托盘切换中英
    public void ToggleAscii()
    {
        lock (_lock)
        {
            _out = new ImeOutput { Preedit = _marked };
            _asciiMode = !_asciiMode;
            if (_asciiMode)
            {
                CommitOrClearPending(true);
                _ui.HideCandidates();
                ClearMarkedText();
            }
            _out.WantCaret = true;
            _ui.ShowModeHint(_asciiMode, _capsLock);
            AsciiModeChanged?.Invoke(_asciiMode);
            QueueOut(_activeClient);
        }
    }

    // 文本服务回报了光标位置；reasonFocus 为聚焦后的首次回报，capsLock 是回报时的大小写锁定状态
    public void CaretReported(long client, bool reasonFocus, bool capsLock)
    {
        lock (_lock)
        {
            if (client != _activeClient) return;
            _capsLock = capsLock;
            if (reasonFocus && AppearancePrefs.ShowCaretModeHint && _marked.Length == 0)
            {
                _ui.ShowModeHint(_asciiMode, _capsLock);
            }
        }
    }

    // ---- 按键主流程 ----

    private bool HandleKeyCore(KeyEvent key, ref bool passToClient)
    {
        if (key.IsUp)
        {
            if (Vk.IsShift(key.Vk)) return HandleShiftUp();
            return false;
        }

        if (Vk.IsModifier(key.Vk))
        {
            // 删除 / 排序窗口开着时，Shift 不要切成英文
            if (Vk.IsShift(key.Vk))
            {
                if (_ui.IsDeletePanelOpen || _ui.IsRankPanelOpen) return false;
                _sawOtherKeyWithShift = false;
                _shiftIsSticky = true;
                return false;
            }
            // Ctrl+Shift 等组合不算单按 Shift
            _sawOtherKeyWithShift = true;
            return false;
        }

        // 窗口自己处理按键（回车、Esc、Tab）
        if (_ui.IsDeletePanelOpen || _ui.IsRankPanelOpen) return false;

        // 开始输入后收起模式提示，避免盖住编码或候选
        _ui.HideModeHint();
        _sawOtherKeyWithShift = true;

        // 候选出现时：造词那组修饰键 + 数字，删除对应候选
        if (HandleDeleteCandidateShortcut(key)) return true;
        // 排序快捷键（默认 Ctrl+Alt+0，可在「输入习惯」页单独改）：拖动排序当前候选。前台软件吃掉这组键时，改由候选条期间的全局热键调用 OpenRankFromHotKey
        if (HandleRankCandidatesShortcut(key)) return true;

        // Win 组合键交给系统
        if (key.Has(KeyMods.Win)) return false;

        if (!_engine.EnsureStarted())
        {
            passToClient = true;
            return false;
        }

        // 回车清码：编码中按回车只清掉编码，不把字母上屏。放在标点之前，避免被 Rime 当作上屏处理
        if (BehaviorPrefs.EnterClearsCode && key.Vk == Vk.Return &&
            !key.Has(KeyMods.Control) && !key.Has(KeyMods.Alt) && !key.Has(KeyMods.Win) &&
            _engine.Snapshot(peekOnly: true)?.IsComposing == true)
        {
            _engine.ClearComposition();
            ClearPunctuation();
            _ui.HideCandidates();
            ClearMarkedText();
            _specialPreedit = "";
            _specialPage = 0;
            return true;
        }

        // 标点在输入法里处理。编码过程中仍交给 Rime，避免打断翻页和选词
        if (!_asciiMode && HandlePunctuationKey(key) is { } punctHandled) return punctHandled;

        // 预置候选不在 Rime 菜单里，选词 / 翻页键需自行处理
        if (!_asciiMode && HandleSpecialCandidateKey(key) is { } specialHandled) return specialHandled;

        // 数字选词
        var snap = _engine.Snapshot(peekOnly: true);
        if (snap is { IsComposing: true } && key.FirstChar is char ch && ch >= '1' && ch <= '9' && !key.Has(KeyMods.Control) && !key.Has(KeyMods.Alt))
        {
            var idx = ch - '1';
            if (idx < snap.Candidates.Count) return SelectCandidate(idx);
        }

        if (_asciiMode)
        {
            passToClient = true;
            return false;
        }

        var mapped = MapKey(key);
        if (mapped == null)
        {
            passToClient = true;
            return false;
        }
        var handled = _engine.ProcessKey(mapped.Value.KeyCode, mapped.Value.Modifiers);
        if (handled)
        {
            ApplyEmptyCodeClear();
            ApplyUniqueFourCodeSelect();
            return ConsumeRimeUpdate();
        }
        if (_engine.Snapshot(peekOnly: true)?.IsComposing == true) return true;
        passToClient = true;
        return false;
    }

    // 按键带回的大写锁定状态变了就更新，并像中英切换一样弹一下模式提示
    private void TrackCapsLock(KeyEvent key)
    {
        var caps = key.Has(KeyMods.CapsLock);
        if (caps == _capsLock) return;
        _capsLock = caps;
        // 先要光标再显示，避免仍用聚焦时错位的旧坐标（与 Shift 归位同一套路）
        _out.WantCaret = true;
        _ui.ShowModeHint(_asciiMode, _capsLock);
    }

    // 单按 Shift 抬起时切换中英
    private bool HandleShiftUp()
    {
        if (_ui.IsDeletePanelOpen || _ui.IsRankPanelOpen)
        {
            _shiftIsSticky = false;
            return false;
        }
        if (_shiftIsSticky && !_sawOtherKeyWithShift)
        {
            _asciiMode = !_asciiMode;
            Log.Write($"asciiMode={_asciiMode}");
            if (_asciiMode)
            {
                CommitOrClearPending(true);
                _ui.HideCandidates();
                ClearMarkedText();
            }
            _out.WantCaret = true;
            _ui.ShowModeHint(_asciiMode, _capsLock);
            AsciiModeChanged?.Invoke(_asciiMode);
            _shiftIsSticky = false;
            return true;
        }
        _shiftIsSticky = false;
        return false;
    }

    // 当前编码命中预置候选时，按设置里的选词 / 翻页键操作；返回 null 表示交给 Rime 继续处理
    private bool? HandleSpecialCandidateKey(KeyEvent key)
    {
        var snap = _engine.Snapshot(peekOnly: true);
        if (snap is not { IsComposing: true } || snap.Preedit is not { } preedit) return null;
        if (DateTimeCandidates.CandidatesFor(preedit) is not { Count: > 0 } special) return null;
        if (key.Has(KeyMods.Control) || key.Has(KeyMods.Alt)) return null;
        var ch = key.FirstChar;
        var pageSize = AppearancePrefs.PageSize;
        var pageCount = (special.Count + pageSize - 1) / pageSize;
        SyncSpecialPage(preedit);

        // 选中当前页第 n 个（0 起）
        bool Pick(int n)
        {
            var index = _specialPage * pageSize + n;
            if (n < pageSize && index < special.Count) CommitSpecial(special[index]);
            return true;
        }

        // 翻页到头 / 到尾即停，不循环
        bool Turn(int delta)
        {
            var target = Math.Clamp(_specialPage + delta, 0, pageCount - 1);
            if (target != _specialPage)
            {
                _specialPage = target;
                ShowCandidates(special, [], preedit);
            }
            return true;
        }

        if (ch is >= '1' and <= '9') return Pick(ch.Value - '1');
        if (key.Vk == Vk.Space || ch == ' ') return Pick(0);
        if (BehaviorPrefs.SecondThirdKeys == SecondThirdKeys.SemicolonApostrophe)
        {
            if (ch == ';') return Pick(1);
            if (ch == '\'') return Pick(2);
        }
        if (PagingDelta(ch) is int d) return Turn(d);
        // PageUp / PageDown 也可翻页
        if (key.Vk == Vk.Prior) return Turn(-1);
        if (key.Vk == Vk.Next) return Turn(1);
        return null;
    }

    // 按当前翻页键方案判断这颗字符是否翻页
    private static int? PagingDelta(char? ch)
    {
        var paging = BehaviorPrefs.PagingKeys;
        var minusEqual = paging is PagingKeys.MinusEqual or PagingKeys.MinusAndComma;
        var commaPeriod = paging is PagingKeys.CommaPeriod or PagingKeys.MinusAndComma;
        var brackets = paging == PagingKeys.Brackets;
        return ch switch
        {
            '-' when minusEqual => -1,
            ',' when commaPeriod => -1,
            '[' when brackets => -1,
            '=' when minusEqual => 1,
            '.' when commaPeriod => 1,
            ']' when brackets => 1,
            _ => null
        };
    }

    // 未组字时拦截标点。返回 null 表示这颗键继续交给 Rime
    private bool? HandlePunctuationKey(KeyEvent key)
    {
        if (key.Has(KeyMods.Control) || key.Has(KeyMods.Alt)) return null;
        var composing = _engine.Snapshot(peekOnly: true)?.IsComposing == true;
        var ch = string.IsNullOrEmpty(key.Ch) ? null : key.Ch;

        if (_punctSymbols.Length > 0)
        {
            if (key.Vk == Vk.Escape)
            {
                ClearPunctuation();
                return true;
            }
            if (HandlePunctuationNavigation(key)) return true;
            // 标点候选开着时再按标点，改输出这一颗
            if (ch != null && !composing && PunctuationPrefs.ItemFor(ch) is { } again)
            {
                ApplyPunctuation(again);
                return true;
            }
            ClearPunctuation();
            return null;
        }

        if (composing || ch == null || PunctuationPrefs.ItemFor(ch) is not { } item) return null;
        ApplyPunctuation(item);
        return true;
    }

    // 标点候选的选词和翻页。处理了返回 true
    private bool HandlePunctuationNavigation(KeyEvent key)
    {
        var pageSize = AppearancePrefs.PageSize;
        var pageCount = Math.Max((_punctSymbols.Length + pageSize - 1) / pageSize, 1);
        bool Pick(int n)
        {
            var index = _punctPage * pageSize + n;
            if (n < pageSize && index < _punctSymbols.Length) CommitPunctuation(_punctSymbols[index]);
            return true;
        }
        bool Turn(int delta)
        {
            var target = Math.Clamp(_punctPage + delta, 0, pageCount - 1);
            if (target != _punctPage)
            {
                _punctPage = target;
                ShowPunctuationPage();
            }
            return true;
        }
        var ch = key.FirstChar;
        if (ch is >= '1' and <= '9') return Pick(ch.Value - '1');
        if (key.Vk == Vk.Space || ch == ' ') return Pick(0);
        if (PagingDelta(ch) is int d) return Turn(d);
        if (key.Vk == Vk.Prior) return Turn(-1);
        if (key.Vk == Vk.Next) return Turn(1);
        return false;
    }

    private void ApplyPunctuation(PunctuationPrefs.Item item)
    {
        var symbols = PunctuationPrefs.SymbolsFor(item.Key);
        var mode = PunctuationPrefs.ModeFor(item.Key);
        var primary = symbols.Length > 0 ? symbols[0] : item.Key;
        // 数字后的半角标点单独处理，不走候选列表
        if (HalfWidthPunctuationDecision(primary) is { } decision)
        {
            CommitPunctuation(decision.Text, decision.Replace, decision.FollowsDigit);
            return;
        }
        if (mode == PunctuationMode.Direct)
        {
            CommitPunctuation(_quotePair.DirectSymbol(item.Key, symbols));
            return;
        }
        _punctSymbols = symbols;
        _punctPage = 0;
        ShowPunctuationPage();
    }

    private void ShowPunctuationPage()
    {
        var pageSize = AppearancePrefs.PageSize;
        var start = Math.Min(_punctPage * pageSize, Math.Max(_punctSymbols.Length - 1, 0));
        var page = _punctSymbols.Skip(start).Take(pageSize).ToList();
        _onCandidateClick = idx =>
        {
            var index = _punctPage * pageSize + idx;
            if (index < _punctSymbols.Length) CommitPunctuation(_punctSymbols[index]);
            return true;
        };
        _ui.ShowCandidates(new CandidatePage(page, []));
    }

    private void CommitPunctuation(string text, int replace = 0, bool followsDigit = false)
    {
        InsertText(text, replace);
        // 半角是跟着数字上的，记下它，下一次同一键才能换回中文
        if (followsDigit)
        {
            _recent = RecentKind.HalfWidthAfterDigit;
            _recentHalfWidth = text;
        }
        ClearPunctuation();
    }

    // 上一次输入是数字时上屏半角；再按一次，把这个半角换回中文标点
    private (string Text, int Replace, bool FollowsDigit)? HalfWidthPunctuationDecision(string primary)
    {
        (string Chinese, string Ascii)[] rules = [("。", "."), ("，", ","), ("：", ":")];
        var rule = rules.FirstOrDefault(r => r.Chinese == primary);
        if (rule.Chinese == null) return null;
        return _recent switch
        {
            RecentKind.Digit => (rule.Ascii, 0, true),
            RecentKind.HalfWidthAfterDigit when _recentHalfWidth == rule.Ascii => (rule.Chinese, _recentHalfWidth.Length, false),
            _ => null
        };
    }

    private static bool IsPlainDigit(char c) => c is >= '0' and <= '9' or >= '０' and <= '９';

    // 直通给当前软件的按键不会经过 InsertText，这里单独记上一次输入
    private void NoteKeyPassedToClient(KeyEvent key)
    {
        if (key.Has(KeyMods.Control) || key.Has(KeyMods.Alt) || key.Has(KeyMods.Win)) return;
        switch (key.Vk)
        {
            case Vk.Left or Vk.Right or Vk.Up or Vk.Down or Vk.Home or Vk.End or Vk.Prior or Vk.Next or Vk.Back or Vk.Delete:
                _recent = RecentKind.Other;
                return;
            case Vk.Return:
                _recent = RecentKind.Other;
                return;
        }
        if (string.IsNullOrEmpty(key.Ch) || key.Ch.Length != 1) return;
        if (key.Ch[0] is '\r' or '\n')
        {
            _recent = RecentKind.Other;
            return;
        }
        NoteRecentInput(key.Ch);
    }

    // 上一次真正写进去的文字。只有末尾是数字，下一次才走半角标点
    private void NoteRecentInput(string s)
    {
        if (s.Length == 0) return;
        _recent = IsPlainDigit(s[^1]) ? RecentKind.Digit : RecentKind.Other;
    }

    private void ClearPunctuation()
    {
        if (_punctSymbols.Length > 0) _ui.HideCandidates();
        _punctSymbols = [];
        _punctPage = 0;
    }

    // 编码变化时回到预置候选第一页
    private void SyncSpecialPage(string preedit)
    {
        if (preedit == _specialPreedit) return;
        _specialPreedit = preedit;
        _specialPage = 0;
    }

    // 预置候选上屏并清空 Rime 编码
    private void CommitSpecial(string text)
    {
        InsertText(text);
        _engine.ClearComposition();
        _ui.HideCandidates();
        ClearMarkedText();
        _specialPreedit = "";
        _specialPage = 0;
    }

    // 空码清空：四码打完仍没有候选时清空编码
    private void ApplyEmptyCodeClear()
    {
        if (!BehaviorPrefs.EmptyCodeClear) return;
        var snap = _engine.Snapshot(peekOnly: true);
        if (snap is not { IsComposing: true } || snap.Preedit is not { } preedit) return;
        if (!IsFullFourCode(CodeOf(preedit)) || snap.Candidates.Count > 0) return;
        if (DateTimeCandidates.CandidatesFor(preedit) != null) return;
        _engine.ClearComposition();
    }

    // 四码唯一：打满四码且整表只有一个候选时上屏第一项
    private void ApplyUniqueFourCodeSelect()
    {
        if (!BehaviorPrefs.AutoSelectUniqueFourCode) return;
        var snap = _engine.Snapshot(peekOnly: true);
        if (snap is not { IsComposing: true } || snap.Preedit is not { } preedit) return;
        if (!IsFullFourCode(CodeOf(preedit))) return;
        if (DateTimeCandidates.CandidatesFor(preedit) != null) return;
        if (_engine.AllCandidateTexts().Count != 1) return;
        _engine.SelectCandidateOnPage(0);
    }

    // 当前实际输入的编码。RawInput 在反查时仍带 z，Preedit 可能已被格式化，以 RawInput 为准
    private string CodeOf(string preedit)
    {
        var typed = _engine.RawInput();
        return string.IsNullOrEmpty(typed) ? preedit : typed;
    }

    private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    // 恰好四码字母且不以 z 开头（z 是反查前缀）
    private static bool IsFullFourCode(string code)
    {
        if (code.Length != 4 || code[0] is 'z' or 'Z') return false;
        foreach (var c in code)
        {
            if (!IsLetter(c)) return false;
        }
        return true;
    }

    private bool ConsumeRimeUpdate()
    {
        var snap = _engine.Snapshot();
        if (snap == null) return true;
        if (!string.IsNullOrEmpty(snap.CommitText))
        {
            InsertText(snap.CommitText);
            ClearMarkedText();
        }
        var preedit = snap.Preedit ?? "";
        var special = DateTimeCandidates.CandidatesFor(preedit);
        var texts = special ?? snap.Candidates.Select(c => c.Text).ToList();
        var comments = special == null ? snap.Candidates.Select(c => c.Comment ?? "").ToList() : [];
        if (snap.IsComposing || texts.Count > 0)
        {
            SetMarkedText(preedit);
            ShowCandidates(texts, comments, preedit);
        }
        else
        {
            _ui.HideCandidates();
            _onCandidateClick = null;
            if (string.IsNullOrEmpty(snap.CommitText)) ClearMarkedText();
        }
        return true;
    }

    private bool SelectCandidate(int index)
    {
        var preedit = _engine.Snapshot(peekOnly: true)?.Preedit ?? "";
        if (DateTimeCandidates.CandidatesFor(preedit) is { } special)
        {
            // index 为当前页内序号，换算到整张预置列表
            SyncSpecialPage(preedit);
            var absolute = _specialPage * AppearancePrefs.PageSize + index;
            if (absolute < special.Count) CommitSpecial(special[absolute]);
            return true;
        }
        if (!_engine.SelectCandidateOnPage(index)) return true;
        return ConsumeRimeUpdate();
    }

    private void ShowCandidates(List<string> texts, List<string> rimeComments, string preedit)
    {
        // 没有候选时只保留编码下划线，不弹出空候选窗
        if (texts.Count == 0)
        {
            _ui.HideCandidates();
            _onCandidateClick = null;
            return;
        }
        var pageSize = AppearancePrefs.PageSize;
        var page = texts.Take(pageSize).ToList();
        if (DateTimeCandidates.CandidatesFor(preedit) != null)
        {
            // 预置候选按当前页切片；Rime 候选本身已是当前页
            SyncSpecialPage(preedit);
            var start = Math.Min(_specialPage * pageSize, Math.Max(texts.Count - 1, 0));
            page = texts.Skip(start).Take(pageSize).ToList();
        }
        var codes = CandidateCodes(page, preedit);
        var comments = CandidateComments(rimeComments, page, preedit);
        _onCandidateClick = SelectCandidate;
        _ui.ShowCandidates(new CandidatePage(page, codes, comments));
    }

    // 备注：Rime comment 优先；没有时取码表第四列
    private List<string> CandidateComments(List<string> rime, List<string> texts, string preedit)
    {
        if (!AppearancePrefs.ShowCommentInPanel || DateTimeCandidates.CandidatesFor(preedit) != null) return [];
        var typed = _engine.RawInput();
        var input = typed.Length == 0 ? preedit : typed;
        var notes = DictLexiconIndex.Shared.DisplayNotes(texts, input);
        var result = new List<string>(texts.Count);
        for (var i = 0; i < texts.Count; i++)
        {
            var value = i < rime.Count ? rime[i].Trim() : "";
            if (value.Length > 0) result.Add(value);
            else result.Add(i < notes.Count ? notes[i] : "");
        }
        return result;
    }

    // 词后编码：普通输入只显示还没打完的后几码；z 反查显示单字五笔码。预置候选没有五笔码
    private List<string> CandidateCodes(List<string> texts, string preedit)
    {
        if (!AppearancePrefs.ShowPreeditInPanel || DateTimeCandidates.CandidatesFor(preedit) != null) return [];
        var typed = _engine.RawInput();
        var input = typed.Length == 0 ? preedit : typed;
        return DictLexiconIndex.Shared.DisplayCodes(texts, input, AppearancePrefs.CodeDetail == CodeDetail.Full);
    }

    // 1–9 的页内序号；数字行和数字键盘都认
    private static int? DigitIndex(KeyEvent key)
    {
        if (key.FirstChar is char c && c >= '1' && c <= '9') return c - '1';
        if (key.Vk >= Vk.D1 && key.Vk <= Vk.D9) return key.Vk - Vk.D1;
        if (key.Vk >= Vk.Numpad1 && key.Vk <= Vk.Numpad9) return key.Vk - Vk.Numpad1;
        return null;
    }

    // 按键上的修饰键
    private static HotKeyModifiers ModifiersOf(KeyEvent key)
    {
        var m = HotKeyModifiers.None;
        if (key.Has(KeyMods.Control)) m |= HotKeyModifiers.Control;
        if (key.Has(KeyMods.Alt)) m |= HotKeyModifiers.Alt;
        if (key.Has(KeyMods.Shift)) m |= HotKeyModifiers.Shift;
        if (key.Has(KeyMods.Win)) m |= HotKeyModifiers.Win;
        return m;
    }

    // 按键的修饰键是否正好等于造词快捷键的修饰键
    private static bool MatchesQuickAddModifiers(KeyEvent key)
    {
        var m = ModifiersOf(key);
        return m != HotKeyModifiers.None && m == QuickAddPrefs.Modifiers;
    }

    // 造词修饰键 + 1–9：弹出删除确认，并说明词库、权重和同编码词
    private bool HandleDeleteCandidateShortcut(KeyEvent key)
    {
        if (!MatchesQuickAddModifiers(key)) return false;
        if (DigitIndex(key) is not int index) return false;
        var snap = _engine.Snapshot(peekOnly: true);
        if (snap is not { IsComposing: true }) return false;
        var preedit = snap.Preedit ?? "";
        if (DateTimeCandidates.CandidatesFor(preedit) != null)
        {
            BeginPanel();
            _ui.PresentDeleteUnavailable("这是预置的日期或时间候选，不在词库里，不能删除。", preedit, RestoreAfterPanel);
            return true;
        }
        if (index >= snap.Candidates.Count) return true;
        var text = snap.Candidates[index].Text.Trim();
        if (text.Length == 0) return true;
        BeginPanel();
        _ui.PresentDeletePanel(text, preedit, RestoreAfterPanel);
        return true;
    }

    // 排序快捷键（默认 Ctrl+Alt+0，可在设置里单独改）：弹出当前候选的排序窗口
    private bool HandleRankCandidatesShortcut(KeyEvent key)
    {
        if (!RankPrefs.Matches((uint)key.Vk, ModifiersOf(key))) return false;
        var show = PrepareRankShow();
        show?.Invoke();
        return show != null;
    }

    // 候选条期间的全局热键。必须在收到热键的界面线程上调用，窗口才能压过当前软件
    public void OpenRankFromHotKey()
    {
        Action? show;
        lock (_lock) show = PrepareRankShow();
        show?.Invoke();
    }

    // 组字中才弹出。返回的动作在锁外执行，避免显示窗口时重入这把锁
    private Action? PrepareRankShow()
    {
        if (_ui.IsDeletePanelOpen || _ui.IsRankPanelOpen) return null;
        var snap = _engine.Snapshot(peekOnly: true);
        if (snap is not { IsComposing: true }) return null;
        var preedit = snap.Preedit ?? "";
        if (DateTimeCandidates.CandidatesFor(preedit) != null)
        {
            BeginPanel();
            return () => _ui.PresentRankUnavailable("这是预置的日期或时间候选，没有词库权重，不能排序。", preedit, RestoreAfterPanel);
        }
        var raw = _engine.RawInput();
        var compact = new string(raw.ToLowerInvariant().Where(c => c < 128 && (char.IsLetter(c) || c == '\'')).ToArray());
        if (ReverseLookup.IsMatch(compact))
        {
            BeginPanel();
            return () => _ui.PresentRankUnavailable("拼音反查的顺序不由五笔权重决定，不能在这里调整。", preedit, RestoreAfterPanel);
        }
        var texts = _engine.AllCandidateTexts().Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        // 整表接口没取到时，用当前页候选，避免吃掉按键却不弹窗
        if (texts.Count == 0) texts = snap.Candidates.Select(c => c.Text.Trim()).Where(t => t.Length > 0).ToList();
        if (texts.Count == 0) return null;
        BeginPanel();
        return () => _ui.PresentRankPanel(preedit, texts, RestoreAfterPanel);
    }

    // 记下打开窗口时的输入框；窗口抢焦点时保留编码
    private void BeginPanel()
    {
        _panelClient = _activeClient;
        _panelPending = true;
        _ui.HideCandidates();
    }

    // 删词 / 排序窗口关闭。sessionReset 为 true 表示词库已重载，需要把编码重新输进去
    public void RestoreAfterPanel(string code, bool sessionReset)
    {
        lock (_lock)
        {
            var target = _panelClient != 0 ? _panelClient : _activeClient;
            _panelPending = false;
            _panelClient = 0;
            _out = new ImeOutput { Preedit = _marked };
            if (!_engine.EnsureStarted())
            {
                QueueOut(target);
                return;
            }
            var composing = _engine.Snapshot(peekOnly: true)?.IsComposing == true;
            if (sessionReset && !composing) RestoreComposition(code);
            else ConsumeRimeUpdate();
            _activeClient = target;
            QueueOut(target);
        }
    }

    // 删词会重建 Rime 会话，把删除前的编码重新输进去
    private void RestoreComposition(string preedit)
    {
        foreach (var c in preedit)
        {
            if (c < 0x20 || c > 0x7E) continue;
            _engine.ProcessKey(c, 0);
        }
        ConsumeRimeUpdate();
    }

    private void InsertText(string text, int replace = 0)
    {
        _ui.NoteCommittedWord(text);
        _out.Commits.Add((text, replace));
        NoteRecentInput(text);
    }

    private void SetMarkedText(string s)
    {
        _marked = s;
        _out.Preedit = s;
    }

    private void ClearMarkedText()
    {
        _marked = "";
        _out.Preedit = "";
    }

    // forceCommitRaw 为 true 时，没有待上屏文字就把编码里的字母数字直接上屏
    private void CommitOrClearPending(bool forceCommitRaw)
    {
        if (!_engine.Started) return;
        var snap = _engine.Snapshot();
        if (snap != null)
        {
            if (!string.IsNullOrEmpty(snap.CommitText))
            {
                InsertText(snap.CommitText);
            }
            else if (forceCommitRaw && !string.IsNullOrEmpty(snap.Preedit))
            {
                var raw = new string(snap.Preedit.Where(char.IsLetterOrDigit).ToArray());
                if (raw.Length > 0) InsertText(raw);
            }
        }
        _engine.ClearComposition();
        _onCandidateClick = null;
    }

    private void FlushReloadIfIdle()
    {
        if (_engine.Snapshot(peekOnly: true)?.IsComposing == true) return;
        if (!UserDictWriter.PeekNeedsDeploy()) return;
        _engine.ReloadUserDictionary();
    }

    private ImeOutput TakeOutput()
    {
        var o = _out;
        _out = new ImeOutput { Preedit = _marked };
        return o;
    }

    // 把这次产生的输出放进待取队列并通知宿主
    private void QueueOut(long client)
    {
        if (client == 0) return;
        var o = TakeOutput();
        if (_pending.TryGetValue(client, out var existing))
        {
            existing.Commits.AddRange(o.Commits);
            existing.Preedit = o.Preedit;
            existing.WantCaret |= o.WantCaret;
        }
        else
        {
            _pending[client] = o;
        }
        OutputPending?.Invoke(client);
    }

    // Windows 虚拟键 → Rime（X11 keysym）按键
    public static (int KeyCode, int Modifiers)? MapKey(KeyEvent key)
    {
        const int shiftMask = 1 << 0, lockMask = 1 << 1, controlMask = 1 << 2, altMask = 1 << 3;
        var mods = 0;
        if (key.Has(KeyMods.Shift)) mods |= shiftMask;
        if (key.Has(KeyMods.CapsLock)) mods |= lockMask;
        if (key.Has(KeyMods.Control)) mods |= controlMask;
        if (key.Has(KeyMods.Alt)) mods |= altMask;
        int? special = key.Vk switch
        {
            Vk.Space => 0x20,
            Vk.Return => 0xff0d,
            Vk.Back => 0xff08,
            Vk.Delete => 0xffff,
            Vk.Escape => 0xff1b,
            Vk.Tab => 0xff09,
            Vk.Left => 0xff51,
            Vk.Right => 0xff53,
            Vk.Up => 0xff52,
            Vk.Down => 0xff54,
            Vk.Prior => 0xff55,
            Vk.Next => 0xff56,
            Vk.Home => 0xff50,
            Vk.End => 0xff57,
            _ => null
        };
        if (special is int s) return (s, mods);
        // 可打印字符：Shift 组合直接送变换后的字符，不带修饰键
        if (key.FirstChar is char c && c >= 0x20 && c <= 0x7E)
        {
            if (key.Has(KeyMods.Shift) && !key.Has(KeyMods.Control) && !key.Has(KeyMods.Alt)) return (c, 0);
            // Ctrl / Alt 组合按小写字母送，Rime 的 emacs 绑定按小写匹配
            var lower = char.ToLowerInvariant(c);
            return (lower, mods & ~shiftMask);
        }
        return null;
    }
}
