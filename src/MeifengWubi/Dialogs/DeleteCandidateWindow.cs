using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meifeng.Core.Engine;
using Meifeng.Core.Lexicon;
using MeifengWubi.Ui;

namespace MeifengWubi.Dialogs;

// 删除候选确认（对应 macOS DeleteCandidatePanel）：显示词库、权重、备注和同编码词，确认后从所选词库删除
internal sealed class DeleteCandidateWindow : ImeDialog
{
    private static DeleteCandidateWindow? _current;

    private readonly TextBlock _word = UiKit.Label("", 13, FontWeights.Medium);
    private readonly TextBlock _code = UiKit.Label("", 13);
    private readonly ComboBox _source = new() { MinWidth = 220, FontSize = 13 };
    private readonly TextBlock _weight = UiKit.Label("", 13);
    private readonly TextBlock _note = UiKit.Label("", 13, wrap: true);
    private readonly TextBlock _extra = UiKit.Label("", 11, secondary: true, wrap: true);
    private readonly LexiconTable _peers = new(168);
    private readonly TextBlock _status = UiKit.Label("", 11, secondary: true, wrap: true);
    private readonly Button _delete = new() { Content = "删除", IsDefault = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5) };

    private readonly string _preedit;
    private readonly Action<string, bool> _onClosed;
    private List<DictLexiconHit> _hits = [];
    private bool _reloaded;

    public static bool IsOpen => _current != null;

    // 用候选与当前编码打开；关闭时把编码交回输入框
    public static void Present(string candidate, string preedit, Action<string, bool> onClosed)
    {
        _current?.Close();
        var w = new DeleteCandidateWindow(preedit, onClosed);
        _current = w;
        w.Load(candidate);
        w.ShowAndActivate();
    }

    // 预置候选等不能删除时只说明原因
    public static void PresentUnavailable(string reason, string preedit, Action<string, bool> onClosed)
    {
        _current?.Close();
        var w = new DeleteCandidateWindow(preedit, onClosed);
        _current = w;
        w.Fill("—", "—", []);
        w._peers.ShowMessage("");
        UiKit.SetTone(w._status, reason, Tone.Attention);
        w._delete.IsEnabled = false;
        w.ShowAndActivate();
    }

    private DeleteCandidateWindow(string preedit, Action<string, bool> onClosed) : base("删除候选", 520)
    {
        _preedit = preedit;
        _onClosed = onClosed;
        _source.SelectionChanged += (_, _) => ApplySelectedSource();
        _delete.SetResourceReference(StyleProperty, "AccentButtonStyle");
        _delete.Click += (_, _) => ConfirmDelete();
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5) };

        var form = UiKit.VStack(8,
            UiKit.FormRow("词条", 36, _word),
            UiKit.FormRow("编码", 36, _code),
            UiKit.FormRow("词库", 36, _source),
            UiKit.FormRow("权重", 36, _weight),
            UiKit.FormRow("备注", 36, _note));
        _code.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        var footer = UiKit.HStack(8, cancel, _delete);
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        var root = UiKit.VStack(8, form, _extra, UiKit.Label("同编码词条", 12, FontWeights.Medium), _peers, _status, footer);
        root.Margin = new Thickness(16, 12, 16, 12);
        Content = root;
        // 关闭前交回编码；放在 Closing 里，保证先于焦点切走时的失焦通知
        Closing += (_, _) =>
        {
            if (_current == this) _current = null;
            _onClosed(_preedit, _reloaded);
        };
    }

    // 主词库很大，后台查找来源，避免卡住界面
    private async void Load(string candidate)
    {
        Fill(candidate, "", []);
        _peers.ShowMessage("正在查找词库…");
        UiKit.SetTone(_status, "正在查找这个词来自哪个词库。", Tone.Idle);
        _delete.IsEnabled = false;
        var match = await Task.Run(() => DictLexiconIndex.Shared.Match(candidate, _preedit));
        if (!IsLoaded && !IsVisible) return;
        _hits = match.Hits.OrderBy(CandidateRankWeight.SourceRank).ToList();
        Fill(candidate, _hits.FirstOrDefault()?.Entry.Code ?? "", match.OtherCodes);
        if (_hits.Count == 0)
        {
            _peers.ShowMessage("没有同编码词条。");
            UiKit.SetTone(_status, $"没有在词库里找到「{candidate}」。它可能是标点或临时候选，不能删除。", Tone.Attention);
            _delete.IsEnabled = false;
        }
        else
        {
            UiKit.SetTone(_status, "确认后会从所选词库删掉这一条，并重新加载词库。", Tone.Idle);
            _delete.IsEnabled = true;
        }
    }

    private void Fill(string candidate, string code, List<string> otherCodes)
    {
        _word.Text = candidate;
        _code.Text = code.Length == 0 ? "—" : code;
        _source.Items.Clear();
        if (_hits.Count == 0)
        {
            _source.Items.Add("未找到");
            _source.SelectedIndex = 0;
            _source.IsEnabled = false;
            _weight.Text = "—";
            _note.Text = "—";
        }
        else
        {
            foreach (var hit in _hits) _source.Items.Add(hit.SourceTitle);
            _source.IsEnabled = _hits.Count > 1;
            _source.SelectedIndex = 0;
        }
        _extra.Text = otherCodes.Count == 0 ? "" : $"该词还有编码 {string.Join("、", otherCodes)}";
        _extra.Visibility = otherCodes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplySelectedSource()
    {
        var index = _source.SelectedIndex;
        if (index < 0 || index >= _hits.Count) return;
        var hit = _hits[index];
        _code.Text = hit.Entry.Code;
        _weight.Text = hit.Entry.Weight?.ToString() ?? "无";
        var note = hit.Entry.Note.Trim();
        _note.Text = note.Length == 0 ? "无" : note;
        var peers = DictLexiconIndex.Shared.Hits(hit.Entry.Code);
        if (peers.Count == 0) _peers.ShowMessage("没有同编码词条。");
        else _peers.ShowHits(peers);
    }

    private async void ConfirmDelete()
    {
        var index = _source.SelectedIndex;
        if (!_delete.IsEnabled || index < 0 || index >= _hits.Count) return;
        var hit = _hits[index];
        _delete.IsEnabled = false;
        UiKit.SetTone(_status, $"正在从「{hit.SourceTitle}」删除并重新加载…", Tone.Idle);
        try
        {
            _reloaded = await Task.Run(() =>
            {
                DictLexiconIndex.Shared.Remove(hit);
                return RimeEngine.Shared.ReloadUserDictionary();
            });
            Close();
        }
        catch (Exception ex)
        {
            _delete.IsEnabled = true;
            UiKit.SetTone(_status, ex.Message, Tone.Error);
        }
    }
}
