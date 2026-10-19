using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meifeng.Core.Config;
using Meifeng.Core.Engine;
using Meifeng.Core.Lexicon;
using MeifengWubi.Ui;

namespace MeifengWubi.Dialogs;

// 全局造词窗（对应 macOS QuickAddWordPanel）：词条自动编码，写入用户词所选分组后重载词库
internal sealed class QuickAddWindow : ImeDialog
{
    private static QuickAddWindow? _current;
    private static string _lastCommittedWord = "";
    private static DateTime? _lastCommittedAt;
    private static readonly object CommittedLock = new();

    private readonly TextBox _word = UiKit.Field(placeholder: "输入后回车添加");
    private readonly TextBox _code = UiKit.Field(placeholder: "自动生成，可修改", mono: true);
    private readonly TextBox _weight = UiKit.Field(placeholder: "可空", mono: true);
    private readonly TextBlock _placement = UiKit.Label("", 12, secondary: true);
    private readonly ComboBox _group = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _sameTitle = UiKit.Label("同编码词条", 12, FontWeights.Medium);
    private readonly LexiconTable _same = new(140);
    private readonly TextBlock _status = UiKit.Label("", 11, secondary: true, wrap: true);
    private readonly List<int> _groupIndexes = [];
    private List<DictLexiconHit> _sameHits = [];
    private bool _filling;

    // 候选上屏后记下来，供接下来打开的造词窗预填（引擎线程调用）
    public static void NoteCommittedWord(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.EnumerateRunes().Any(WubiDictEncoder.IsChineseHanzi)) return;
        lock (CommittedLock)
        {
            _lastCommittedWord = trimmed;
            _lastCommittedAt = DateTime.Now;
        }
    }

    // 两分钟内选过的词才自动填入
    private static string? RecentCommittedWord()
    {
        lock (CommittedLock)
        {
            if (_lastCommittedAt is not { } at || (DateTime.Now - at).TotalSeconds >= 120) return null;
            return _lastCommittedWord.Length == 0 ? null : _lastCommittedWord;
        }
    }

    // 快捷键触发时先读前台软件的选区（UI 自动化），读不到再用最近上屏的词
    public static async void Present()
    {
        string? selection = null;
        if (_current is not { IsActive: true }) selection = await ReadSelectionAsync();
        _current ??= new QuickAddWindow();
        _current.ShowWith(selection ?? RecentCommittedWord() ?? "");
    }

    private QuickAddWindow() : base("添加自造词", 440)
    {
        _word.FontSize = 13;
        _code.FontSize = 13;
        _weight.FontSize = 13;
        _weight.HorizontalAlignment = HorizontalAlignment.Stretch;
        _word.TextChanged += (_, _) =>
        {
            if (_filling) return;
            var encoded = WubiDictEncoder.Encode(_word.Text.Trim());
            if (encoded.Length > 0) UiKit.Set(_code, encoded);
            RefreshSameCode();
        };
        _code.TextChanged += (_, _) =>
        {
            if (!_filling) RefreshSameCode();
        };
        _weight.TextChanged += (_, _) => UpdatePlacementHint();
        foreach (var box in new[] { _word, _code, _weight }) box.PreviewKeyDown += OnFieldKey;

        var add = new Button { Content = "添加", IsDefault = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5) };
        add.SetResourceReference(StyleProperty, "AccentButtonStyle");
        add.Click += (_, _) => CommitWord(false);
        var addClose = new Button { Content = "添加并关闭", MinWidth = 96, Padding = new Thickness(12, 4, 12, 5) };
        addClose.Click += (_, _) => CommitWord(true);
        var footer = UiKit.HStack(8, add, addClose);
        footer.HorizontalAlignment = HorizontalAlignment.Center;

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(form, "词条", _word);
        AddRow(form, "编码", _code);
        AddRow(form, "权重", WeightControls());
        AddRow(form, "分组", _group);

        var root = UiKit.VStack(8, form, _sameTitle, _same, _status, footer);
        root.Margin = new Thickness(16, 10, 16, 12);
        Content = root;
        // 关闭只是隐藏，下次快捷键直接复用
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    private static void AddRow(Grid grid, string title, FrameworkElement control)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = UiKit.Label(title, 13);
        label.VerticalAlignment = control is StackPanel ? VerticalAlignment.Top : VerticalAlignment.Center;
        label.Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0);
        control.Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0);
        Grid.SetRow(label, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(label);
        grid.Children.Add(control);
    }

    // 输入框和名次一行；n 为同编码词最大权重，其余按钮在此基础上加减
    private StackPanel WeightControls()
    {
        var field = new Grid();
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _placement.Margin = new Thickness(8, 0, 0, 0);
        _placement.ToolTip = "按当前权重，在同编码词里的名次。留空按 0 算。";
        Grid.SetColumn(_placement, 1);
        field.Children.Add(_weight);
        field.Children.Add(_placement);
        var steps = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (title, delta) in new[] { ("n", 0), ("-10", -10), ("-1", -1), ("+1", 1), ("+10", 10) })
        {
            var b = UiKit.Button(title, () => ApplyWeightStep(delta));
            b.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
            b.Margin = new Thickness(0, 0, 4, 0);
            steps.Children.Add(b);
        }
        return UiKit.VStack(6, field, steps);
    }

    private void ShowWith(string prefill)
    {
        ReloadGroups();
        _filling = true;
        UiKit.Set(_word, prefill);
        UiKit.Set(_code, prefill.Length == 0 ? "" : WubiDictEncoder.Encode(prefill));
        UiKit.Set(_weight, "");
        _filling = false;
        RefreshSameCode();
        UiKit.SetTone(_status, "", Tone.Idle);
        _status.Visibility = Visibility.Collapsed;
        if (!IsVisible) WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowAndActivate();
        _word.Focus();
        _word.SelectAll();
    }

    private void SetStatus(string message, Tone tone)
    {
        UiKit.SetTone(_status, message, tone);
        _status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ReloadGroups()
    {
        var document = UserDictWriter.LoadActiveDocument();
        _group.Items.Clear();
        _groupIndexes.Clear();
        IReadOnlyList<UserDictGroup> groups = document.Groups.Count == 0 ? [new UserDictGroup("")] : document.Groups;
        for (var i = 0; i < groups.Count; i++)
        {
            _group.Items.Add(groups[i].DisplayName);
            _groupIndexes.Add(i);
        }
        _group.SelectedIndex = Math.Min(QuickAddPrefs.SelectedGroupIndex, Math.Max(0, _group.Items.Count - 1));
    }

    // 回车直接添加；输入法组字时回车被吃掉，这里收到的是 ImeProcessed，不会误触
    private void OnFieldKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitWord(false);
    }

    private string CurrentCode => _code.Text.Trim().ToLowerInvariant();

    // 按当前编码列出各词库里已有的同码词
    private async void RefreshSameCode()
    {
        var code = CurrentCode;
        var show = code.Length > 0;
        _sameTitle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _same.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _sameHits = [];
        UpdatePlacementHint();
        if (!show) return;
        _sameTitle.Text = "同编码词条";
        _same.ShowMessage("正在查找…");
        var hits = await Task.Run(() => DictLexiconIndex.Shared.Hits(code));
        if (CurrentCode != code) return;
        _sameHits = hits;
        _sameTitle.Text = hits.Count == 0 ? "同编码词条" : $"同编码词条（{hits.Count}）";
        UpdatePlacementHint();
        if (hits.Count == 0) _same.ShowMessage("当前词库里没有这个编码。");
        else _same.ShowHits(hits);
    }

    // 按当前权重，这个词会排在同编码词的第几位。留空权重按 0
    private void UpdatePlacementHint()
    {
        var code = CurrentCode;
        var text = _weight.Text.Trim();
        if (code.Length == 0)
        {
            _placement.Text = "";
            return;
        }
        int weight;
        if (text.Length == 0) weight = 0;
        else if (!int.TryParse(text, out weight))
        {
            _placement.Text = "";
            return;
        }
        var word = _word.Text.Trim();
        var ahead = _sameHits.Where(h => h.Entry.Text != word || h.Entry.Code != code).Count(h => (h.Entry.Weight ?? 0) > weight);
        _placement.Text = $"第 {ahead + 1} 位";
    }

    private void ApplyWeightStep(int delta)
    {
        var hits = DictLexiconIndex.Shared.Hits(CurrentCode);
        _sameHits = hits;
        var weights = hits.Where(h => h.Entry.Weight != null).Select(h => h.Entry.Weight!.Value).ToList();
        var baseWeight = weights.Count == 0 ? 0 : weights.Max();
        UiKit.Set(_weight, (baseWeight + delta).ToString());
        UpdatePlacementHint();
    }

    // 写入当前分组。closeAfter 为真时成功后关掉窗口
    private async void CommitWord(bool closeAfter)
    {
        var code = CurrentCode;
        var text = _word.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("词条不能为空。", Tone.Attention);
            return;
        }
        if (code.Length is < 1 or > 4)
        {
            SetStatus(code.Length == 0 ? "无法自动编码。请到「词库目录」生成单字字典，或手填编码。" : "编码需 1–4 位。", Tone.Attention);
            return;
        }
        var popupIndex = _group.SelectedIndex;
        if (popupIndex < 0 || popupIndex >= _groupIndexes.Count)
        {
            SetStatus("请选择分组。", Tone.Attention);
            return;
        }
        var groupIndex = _groupIndexes[popupIndex];
        var weightText = _weight.Text.Trim();
        int? weight = null;
        if (weightText.Length > 0)
        {
            if (!int.TryParse(weightText, out var value))
            {
                SetStatus("权重需为整数，或留空。", Tone.Attention);
                return;
            }
            weight = value;
        }
        var groupTitle = _group.SelectedItem as string ?? "当前分组";
        try
        {
            // 只重载用户词，不完整部署，避免正在打字的会话中断
            var reloaded = await Task.Run(() =>
            {
                UserDictWriter.AddEntry(new UserDictEntry(code, text, weight), groupIndex);
                DictLexiconIndex.Shared.Invalidate();
                return RimeEngine.Shared.ReloadUserDictionary();
            });
            QuickAddPrefs.SelectedGroupIndex = groupIndex;
            if (!reloaded)
            {
                SetStatus("已写入，但词库重载失败。", Tone.Error);
                return;
            }
            if (closeAfter)
            {
                Hide();
                return;
            }
            SetStatus($"已添加到「{groupTitle}」。", Tone.Idle);
            _filling = true;
            UiKit.Set(_word, "");
            UiKit.Set(_code, "");
            UiKit.Set(_weight, "");
            _filling = false;
            RefreshSameCode();
            _word.Focus();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, Tone.Error);
        }
    }

    // 读前台控件的选中文字；跨进程 UI 自动化可能很慢，最多等 600ms
    private static async Task<string?> ReadSelectionAsync()
    {
        var task = Task.Run(() =>
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused == null) return null;
                if (focused.Current.ProcessId == Environment.ProcessId) return null;
                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
                var ranges = ((TextPattern)pattern).GetSelection();
                var sb = new StringBuilder();
                foreach (var range in ranges) sb.Append(range.GetText(200));
                var text = sb.ToString().Trim();
                return text.Length == 0 || text.Length > 100 ? null : text;
            }
            catch
            {
                return null;
            }
        });
        var done = await Task.WhenAny(task, Task.Delay(600));
        return done == task ? task.Result : null;
    }
}
