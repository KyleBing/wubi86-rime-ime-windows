using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Meifeng.Core.Config;
using Meifeng.Core.Engine;
using Meifeng.Core.Lexicon;
using Meifeng.Core.Sync;
using MeifengWubi.Ui;

namespace MeifengWubi.Dialogs;

// 候选排序（对应 macOS CandidateRankPanel）：拖动调整顺序，只改被拖动那一条的权重
internal sealed class CandidateRankWindow : ImeDialog
{
    private const string StepKey = "candidate.rankWeightStep";
    private static CandidateRankWindow? _current;

    // 表格一行：词条、编码、权重、来源词库
    private sealed class RankRow : INotifyPropertyChanged
    {
        public required string Text { get; init; }
        public required string Code { get; init; }
        public int Weight { get; set; }
        public bool HasWeight { get; set; }
        public required string Source { get; init; }
        public DictLexiconHit? Hit { get; init; }
        public bool Edited { get; set; }
        public string WeightText => HasWeight ? Weight.ToString() : "—";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private readonly TextBlock _codeLabel = UiKit.Label("", 13);
    private readonly ListView _table = new() { Height = 280, FontSize = 13 };
    private readonly TextBlock _status = UiKit.Label("", 11, secondary: true, wrap: true);
    private readonly ObservableCollection<RankRow> _rows = [];

    private readonly string _preedit;
    private readonly Action<string, bool> _onClosed;
    private bool _ready, _dirty, _touchedUserDict, _reloaded;
    private int _step = PrefsStore.Shared.GetInt(StepKey) == 10 ? 10 : 5;

    public static bool IsOpen => _current != null;

    public static void Present(string preedit, IReadOnlyList<string> candidates, Action<string, bool> onClosed)
    {
        _current?.Close();
        var w = new CandidateRankWindow(preedit, onClosed);
        _current = w;
        w._codeLabel.Text = preedit.Length == 0 ? "—" : preedit;
        w.Load(candidates);
        w.ShowAndActivate();
    }

    // 预置候选或反查没有可改的五笔权重时，只说明原因
    public static void PresentUnavailable(string reason, string preedit, Action<string, bool> onClosed)
    {
        _current?.Close();
        var w = new CandidateRankWindow(preedit, onClosed);
        _current = w;
        w._codeLabel.Text = "—";
        UiKit.SetTone(w._status, reason, Tone.Attention);
        w.ShowAndActivate();
    }

    private CandidateRankWindow(string preedit, Action<string, bool> onClosed) : base($"候选排序 {ShortcutLabel()}", 520)
    {
        _preedit = preedit;
        _onClosed = onClosed;
        _codeLabel.FontFamily = new FontFamily("Cascadia Mono, Consolas");

        var step = UiKit.Segmented(["5", "10"], _step == 10 ? 1 : 0, i =>
        {
            _step = i == 1 ? 10 : 5;
            PrefsStore.Shared.Set(StepKey, _step);
            if (_ready) UiKit.SetTone(_status, $"接下来拖动时，权重按 {_step} 调整。", Tone.Idle);
        }, "被拖动的词相对旁边候选增减的权重");
        var header = UiKit.HStack(8, UiKit.FormRow("编码", 36, _codeLabel), UiKit.FormRow("幅度", 36, step));
        var hint = UiKit.Label("只改被拖动的那一条。权重更高的排在前面。", 11, secondary: true, wrap: true);

        var view = new GridView();
        view.Columns.Add(Column("词条", nameof(RankRow.Text), 150, false));
        view.Columns.Add(Column("编码", nameof(RankRow.Code), 72, true));
        view.Columns.Add(Column("权重", nameof(RankRow.WeightText), 64, true));
        view.Columns.Add(Column("词库", nameof(RankRow.Source), 160, false));
        _table.View = view;
        _table.ItemsSource = _rows;
        ReorderHelper.Attach(_table,
            i => _ready && i < _rows.Count && _rows[i].Hit != null,
            i =>
            {
                if (i < _rows.Count) UiKit.SetTone(_status, $"「{_rows[i].Text}」不在词库里，不能改权重。", Tone.Attention);
            },
            ApplyDrag);

        var done = new Button { Content = "完成", IsDefault = true, IsCancel = true, MinWidth = 88, Padding = new Thickness(12, 4, 12, 5) };
        done.SetResourceReference(StyleProperty, "AccentButtonStyle");
        done.Click += (_, _) => Close();
        done.HorizontalAlignment = HorizontalAlignment.Center;

        var root = UiKit.VStack(8, header, hint, _table, _status, done);
        root.Margin = new Thickness(16, 12, 16, 12);
        Content = root;
        Closing += (_, _) => Finish();
    }

    private static GridViewColumn Column(string title, string path, double width, bool mono)
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new Binding(path));
        if (mono) factory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
        return new GridViewColumn { Header = title, Width = width, CellTemplate = new DataTemplate { VisualTree = factory } };
    }

    // 造词那组修饰键加上 0，例如默认的 Ctrl+Alt+0
    private static string ShortcutLabel()
    {
        var mods = QuickAddPrefs.Modifiers;
        return QuickAddPrefs.DisplayString(mods == HotKeyModifiers.None ? QuickAddPrefs.DefaultModifiers : mods, "0");
    }

    private async void Load(IReadOnlyList<string> candidates)
    {
        UiKit.SetTone(_status, "正在读取词库里的权重…", Tone.Idle);
        var preedit = _preedit;
        var loaded = await Task.Run(() => candidates.Select(t => MakeRow(t, preedit)).ToList());
        if (!IsVisible) return;
        foreach (var r in loaded) _rows.Add(r);
        _ready = true;
        var adjustable = loaded.Count(r => r.Hit != null);
        if (loaded.Count == 0) UiKit.SetTone(_status, "没有可以排序的候选。", Tone.Attention);
        else if (adjustable == 0) UiKit.SetTone(_status, "这些候选不在词库里，不能改权重。", Tone.Attention);
        else UiKit.SetTone(_status, $"拖动一行调整顺序。幅度 {_step} 是相对旁边那条候选的权重差。", Tone.Idle);
    }

    // 同一词有多条时，改当前排在前面的那条：权重大的优先，一样大则改用户词
    private static RankRow MakeRow(string text, string preedit)
    {
        var match = DictLexiconIndex.Shared.Match(text, preedit);
        var hit = CandidateRankWeight.PreferredHit(match.Hits);
        return new RankRow
        {
            Text = text,
            Code = hit?.Entry.Code ?? "—",
            Weight = hit?.Entry.Weight ?? 0,
            HasWeight = hit?.Entry.Weight != null,
            Source = hit?.SourceTitle ?? "不在词库",
            Hit = hit
        };
    }

    // 松手后把被拖动的那一条写成新权重，并按权重放回它实际会排到的位置
    private void ApplyDrag(int from, int to)
    {
        if (!_ready || from < 0 || from >= _rows.Count || from == to) return;
        var row = _rows[from];
        if (row.Hit is not { } hit)
        {
            UiKit.SetTone(_status, $"「{row.Text}」不在词库里，不能改权重。", Tone.Attention);
            return;
        }
        var weights = _rows.Select(r => r.Weight).ToList();
        if (CandidateRankWeight.Placed(weights, from, to, _step) is not int newWeight) return;
        var oldWeight = row.Weight;
        var hadWeight = row.HasWeight;
        if (newWeight == oldWeight && hadWeight)
        {
            UiKit.SetTone(_status, $"幅度 {_step} 没有拉开「{row.Text}」和旁边候选的差距。", Tone.Attention);
            return;
        }
        try
        {
            DictLexiconIndex.Shared.UpdateWeight(hit, newWeight);
        }
        catch (Exception ex)
        {
            UiKit.SetTone(_status, ex.Message, Tone.Error);
            return;
        }
        if (Path.GetFileName(hit.FilePath) == "wubi86_jidian_user.dict.yaml") _touchedUserDict = true;
        _dirty = true;
        _rows.RemoveAt(from);
        row.Weight = newWeight;
        row.HasWeight = true;
        row.Edited = true;
        row.Notify();
        var index = CandidateRankWeight.IndexFor(newWeight, _rows.Select(r => r.Weight));
        _rows.Insert(index, row);
        _table.ScrollIntoView(row);
        if (index == to)
        {
            var shownOld = hadWeight ? oldWeight.ToString() : "无";
            UiKit.SetTone(_status, $"「{row.Text}」权重 {shownOld} → {newWeight}。关闭窗口后重新加载词库。", Tone.Idle);
        }
        else
        {
            UiKit.SetTone(_status, $"幅度 {_step} 大于和旁边候选的差距，「{row.Text}」现在是第 {index + 1} 位，权重 {newWeight}。", Tone.Attention);
        }
    }

    // 改过的权重已经写进码表：重新加载，再把原来的编码交回去
    private void Finish()
    {
        if (_current == this) _current = null;
        _ready = false;
        if (_dirty)
        {
            _reloaded = RimeEngine.Shared.ReloadUserDictionary();
        }
        if (_touchedUserDict) UserDictAutoSync.NoteLocalEdit();
        _onClosed(_preedit, _reloaded);
    }
}
