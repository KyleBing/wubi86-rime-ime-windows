using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Meifeng.Core.Config;
using Meifeng.Core.Engine;
using Meifeng.Core.Lexicon;
using Meifeng.Core.Sync;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 用户词页（对应 macOS UserLexiconSettingsView）：顶部同步条；左分组 / 右词条，均可拖动排序
internal sealed class UserLexiconPage : DockPanel, IDisposable
{
    // 表格行
    private sealed record GroupRow(string Name, string Count);
    private sealed record EntryRow(int Index, string Text, string Code, string Weight, string Note, string NoteTip);

    private readonly CloudBar _cloud = new();
    private readonly TextBox _text = UiKit.Field(placeholder: "词条", width: 216);
    private readonly TextBox _code = UiKit.Field(placeholder: "编码", width: 72, mono: true);
    private readonly TextBlock _tip = UiKit.Label("", 11, secondary: true);
    private readonly ListView _groups = new() { FontSize = 12 };
    private readonly ListView _entries = new() { FontSize = 12 };

    private UserDictDocument _document = UserDictDocument.Empty;
    private int _selectedGroup = QuickAddPrefs.SelectedGroupIndex;
    private bool _reloading;

    public UserLexiconPage()
    {
        Margin = new Thickness(23, 16, 28, 16);
        LastChildFill = true;
        SetDock(_cloud, Dock.Top);
        Children.Add(_cloud);
        _cloud.UserDictChanged += Reload;

        var form = BuildForm();
        form.Margin = new Thickness(0, 12, 0, 8);
        SetDock(form, Dock.Top);
        Children.Add(form);
        Children.Add(BuildSplit());

        UserDictWriter.DidChange += OnExternalChange;
        UserDictAutoSync.UserDictDidChange += OnExternalChange;
        Reload();
    }

    public void Dispose()
    {
        UserDictWriter.DidChange -= OnExternalChange;
        UserDictAutoSync.UserDictDidChange -= OnExternalChange;
        _cloud.Dispose();
    }

    // 其他入口（造词窗、同步）改了用户词
    private void OnExternalChange() => Dispatcher.BeginInvoke(Reload);

    private FrameworkElement BuildForm()
    {
        _text.FontSize = 12;
        _code.FontSize = 12;
        _text.TextChanged += (_, _) =>
        {
            var encoded = WubiDictEncoder.Encode(_text.Text.Trim());
            if (encoded.Length > 0) UiKit.Set(_code, encoded);
        };
        foreach (var box in new[] { _text, _code })
        {
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                AddEntry();
            };
        }
        var dock = new DockPanel { LastChildFill = true };
        var left = UiKit.HStack(6, _text, _code, UiKit.Button("添加", AddEntry), UiKit.Button("重新加载", Reload));
        SetDock(left, Dock.Left);
        dock.Children.Add(left);
        _tip.TextAlignment = TextAlignment.Right;
        _tip.TextTrimming = TextTrimming.CharacterEllipsis;
        _tip.Margin = new Thickness(8, 0, 0, 0);
        dock.Children.Add(_tip);
        return dock;
    }

    private FrameworkElement BuildSplit()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tools = UiKit.HStack(8,
            UiKit.TextButton("添加分组", PromptAddGroup),
            UiKit.TextButton("重命名", PromptRenameGroup),
            UiKit.TextButton("删除分组", DeleteSelectedGroup, UiKit.ErrorBrush));
        tools.Margin = new Thickness(0, 0, 0, 4);
        var leftPane = new DockPanel();
        SetDock(tools, Dock.Top);
        leftPane.Children.Add(tools);
        var groupView = new GridView();
        groupView.Columns.Add(TextColumn("名称", nameof(GroupRow.Name), 138));
        groupView.Columns.Add(TextColumn("数", nameof(GroupRow.Count), 44, secondary: true));
        _groups.View = groupView;
        _groups.SelectionMode = SelectionMode.Single;
        _groups.SelectionChanged += (_, _) =>
        {
            if (_reloading || _groups.SelectedIndex < 0) return;
            _selectedGroup = _groups.SelectedIndex;
            QuickAddPrefs.SelectedGroupIndex = _selectedGroup;
            FillEntries();
        };
        ReorderHelper.Attach(_groups, _ => true, _ => { }, MoveGroup);
        leftPane.Children.Add(_groups);
        grid.Children.Add(leftPane);

        // 右侧词条表与分组表顶端对齐
        var entryView = new GridView();
        entryView.Columns.Add(TextColumn("词条", nameof(EntryRow.Text), 120));
        entryView.Columns.Add(TextColumn("编码", nameof(EntryRow.Code), 64, mono: true, secondary: true));
        entryView.Columns.Add(TextColumn("权重", nameof(EntryRow.Weight), 56, mono: true, secondary: true, right: true));
        entryView.Columns.Add(TextColumn("备注", nameof(EntryRow.Note), 120, secondary: true, tip: nameof(EntryRow.NoteTip)));
        entryView.Columns.Add(new GridViewColumn { Header = "", Width = 64, CellTemplate = ActionsTemplate() });
        _entries.View = entryView;
        _entries.SelectionMode = SelectionMode.Single;
        _entries.MouseDoubleClick += (_, _) =>
        {
            if (_entries.SelectedItem is EntryRow row) EditEntry(row.Index);
        };
        ReorderHelper.Attach(_entries, _ => true, _ => { }, MoveEntry);
        var rightPane = new DockPanel();
        var spacer = new Border { Height = tools.DesiredSize.Height };
        tools.SizeChanged += (_, _) => spacer.Height = tools.ActualHeight + tools.Margin.Bottom;
        SetDock(spacer, Dock.Top);
        rightPane.Children.Add(spacer);
        rightPane.Children.Add(_entries);
        Grid.SetColumn(rightPane, 2);
        grid.Children.Add(rightPane);
        return grid;
    }

    private static GridViewColumn TextColumn(string title, string path, double width, bool mono = false, bool secondary = false, bool right = false, string? tip = null)
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new Binding(path));
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        if (mono) factory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
        if (secondary) factory.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        if (right) factory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
        if (tip != null) factory.SetBinding(FrameworkElement.ToolTipProperty, new Binding(tip));
        return new GridViewColumn { Header = title, Width = width, CellTemplate = new DataTemplate { VisualTree = factory } };
    }

    // 顺序用拖动调整，行内只留编辑和删除
    private DataTemplate ActionsTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        panel.AppendChild(IconButton("\uE70F", "编辑", true));
        panel.AppendChild(IconButton("\uE74D", "删除", false));
        return new DataTemplate { VisualTree = panel };
    }

    private FrameworkElementFactory IconButton(string glyph, string tip, bool edit)
    {
        var b = new FrameworkElementFactory(typeof(Button));
        b.SetValue(ContentControl.ContentProperty, glyph);
        b.SetValue(Control.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));
        b.SetValue(Control.FontSizeProperty, 11.0);
        b.SetValue(Control.PaddingProperty, new Thickness(4, 2, 4, 2));
        b.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        b.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        b.SetValue(CursorProperty, Cursors.Hand);
        b.SetValue(ToolTipProperty, tip);
        b.SetBinding(TagProperty, new Binding(nameof(EntryRow.Index)));
        b.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is not Button { Tag: int index }) return;
            if (edit) EditEntry(index);
            else DeleteEntry(index);
        }));
        return b;
    }

    // 按磁盘重读；始终按分组落盘，保证 ## 行保留
    public void Reload()
    {
        _document = UserDictWriter.LoadActiveDocument();
        if (_document.Groups.Count == 0) _document.Groups.Add(new UserDictGroup(""));
        _document.IsGroupMode = true;
        if (_selectedGroup >= _document.Groups.Count) _selectedGroup = Math.Max(0, _document.Groups.Count - 1);
        QuickAddPrefs.SelectedGroupIndex = _selectedGroup;
        _reloading = true;
        _groups.ItemsSource = _document.Groups.Select(g => new GroupRow(g.DisplayName, g.Entries.Count.ToString())).ToList();
        _groups.SelectedIndex = _selectedGroup;
        _reloading = false;
        FillEntries();
        var t = _tip.Text;
        if (t.Length == 0 || t.StartsWith('共') || t.StartsWith("暂无", StringComparison.Ordinal) || t.StartsWith("分组", StringComparison.Ordinal))
        {
            SetTip($"分组 {_document.Groups.Count} · 词条 {_document.EntryCount}");
        }
        _cloud.Refresh();
    }

    private void FillEntries()
    {
        if (_selectedGroup < 0 || _selectedGroup >= _document.Groups.Count)
        {
            _entries.ItemsSource = null;
            return;
        }
        _entries.ItemsSource = _document.Groups[_selectedGroup].Entries
            .Select((e, i) => new EntryRow(i, e.Text, e.Code, e.Weight?.ToString() ?? "", e.Note.Length == 0 ? "—" : e.Note, e.Note))
            .ToList();
    }

    private void SetTip(string text, Tone tone = Tone.Idle) => UiKit.SetTone(_tip, text, tone);

    private Window? Owner => Window.GetWindow(this);

    private void PromptAddGroup()
    {
        var name = UiKit.Prompt(Owner, "添加分组", "空名称会显示为「- 未命名 -」。", "", "分组名（可空）", "创建");
        if (name == null) return;
        var doc = _document.Clone();
        var group = doc.InsertEmptyGroup(doc.Groups.Count);
        group.Name = name;
        doc.IsGroupMode = true;
        Save(doc, $"已添加分组「{group.DisplayName}」", () => _selectedGroup = Math.Max(0, doc.Groups.Count - 1));
    }

    private void PromptRenameGroup()
    {
        if (_selectedGroup < 0 || _selectedGroup >= _document.Groups.Count) return;
        var name = UiKit.Prompt(Owner, "重命名分组", "", _document.Groups[_selectedGroup].Name, "分组名（可空）", "保存");
        if (name == null) return;
        var doc = _document.Clone();
        doc.Groups[_selectedGroup].Name = name;
        doc.IsGroupMode = true;
        Save(doc, $"已重命名为「{doc.Groups[_selectedGroup].DisplayName}」", deploy: false);
    }

    private void DeleteSelectedGroup()
    {
        if (_selectedGroup < 0 || _selectedGroup >= _document.Groups.Count) return;
        var group = _document.Groups[_selectedGroup];
        var detail = group.Entries.Count == 0 ? "将删除该空分组。" : $"组内 {group.Entries.Count} 条词也会一并删除。";
        if (!UiKit.Confirm(Owner, $"删除分组「{group.DisplayName}」？", detail, "删除", destructive: true)) return;
        var doc = _document.Clone();
        doc.Groups.RemoveAt(_selectedGroup);
        if (doc.Groups.Count == 0) doc.Groups.Add(new UserDictGroup(""));
        doc.IsGroupMode = true;
        Save(doc, "已删除分组", () => _selectedGroup = Math.Min(_selectedGroup, Math.Max(0, doc.Groups.Count - 1)));
    }

    private void AddEntry()
    {
        var code = _code.Text.Trim().ToLowerInvariant();
        var text = _text.Text.Trim();
        if (code.Length is < 1 or > 4 || text.Length == 0)
        {
            SetTip("编码需 1–4 位，词条非空。", Tone.Attention);
            return;
        }
        if (_selectedGroup < 0 || _selectedGroup >= _document.Groups.Count)
        {
            SetTip("请先选择左侧分组。", Tone.Attention);
            return;
        }
        var doc = _document.Clone();
        try
        {
            UserDictWriter.AddEntry(new UserDictEntry(code, text), doc.Groups[_selectedGroup].Id, doc);
        }
        catch (Exception e)
        {
            SetTip(e.Message, Tone.Error);
            return;
        }
        var name = doc.Groups[_selectedGroup].DisplayName;
        Save(doc, $"已添加到「{name}」", () =>
        {
            UiKit.Set(_code, "");
            UiKit.Set(_text, "");
        });
    }

    private void DeleteEntry(int row)
    {
        if (_selectedGroup < 0 || _selectedGroup >= _document.Groups.Count) return;
        var entries = _document.Groups[_selectedGroup].Entries;
        if (row < 0 || row >= entries.Count) return;
        var entry = entries[row];
        var doc = _document.Clone();
        doc.Groups[_selectedGroup].Entries.RemoveAt(row);
        Save(doc, $"已删除「{entry.Text}」");
    }

    private void EditEntry(int row)
    {
        if (_selectedGroup < 0 || _selectedGroup >= _document.Groups.Count) return;
        var entries = _document.Groups[_selectedGroup].Entries;
        if (row < 0 || row >= entries.Count) return;
        if (EntryEditDialog.Show(Owner, entries[row]) is not { } edited) return;
        var (newText, newCode, weightText, newNote) = edited;
        if (newText.Length == 0 || newCode.Length is < 1 or > 4)
        {
            SetTip("编码需 1–4 位，词条非空。", Tone.Attention);
            return;
        }
        int? newWeight = null;
        if (weightText.Length > 0)
        {
            if (!int.TryParse(weightText, out var w))
            {
                SetTip("权重需要是整数，或留空。", Tone.Attention);
                return;
            }
            newWeight = w;
        }
        if (entries.Where((e, i) => i != row && e.Text == newText && e.Code == newCode).Any())
        {
            SetTip("本组已有相同词条和编码。", Tone.Attention);
            return;
        }
        var doc = _document.Clone();
        doc.Groups[_selectedGroup].Entries[row] = new UserDictEntry(newCode, newText, newWeight, newNote);
        Save(doc, $"已更新「{newText}」");
    }

    // 拖动调整词条顺序
    private void MoveEntry(int from, int to)
    {
        if (_selectedGroup < 0 || _selectedGroup >= _document.Groups.Count) return;
        var doc = _document.Clone();
        var entries = doc.Groups[_selectedGroup].Entries;
        if (from < 0 || from >= entries.Count) return;
        to = Math.Clamp(to, 0, entries.Count - 1);
        var entry = entries[from];
        entries.RemoveAt(from);
        entries.Insert(to, entry);
        Save(doc, "已调整顺序", () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _entries.SelectedIndex = to;
                if (_entries.SelectedItem != null) _entries.ScrollIntoView(_entries.SelectedItem);
            });
        });
    }

    // 调整分组顺序；当前选中的分组跟着移动
    private void MoveGroup(int from, int to)
    {
        var doc = _document.Clone();
        if (from < 0 || from >= doc.Groups.Count) return;
        to = Math.Clamp(to, 0, doc.Groups.Count - 1);
        var selectedId = _selectedGroup >= 0 && _selectedGroup < doc.Groups.Count ? doc.Groups[_selectedGroup].Id : (Guid?)null;
        var group = doc.Groups[from];
        doc.Groups.RemoveAt(from);
        doc.Groups.Insert(to, group);
        Save(doc, "已调整分组顺序", () =>
        {
            if (selectedId is { } id) _selectedGroup = Math.Max(0, doc.Groups.FindIndex(g => g.Id == id));
        });
    }

    // 落盘、刷新表格，再在后台重载词库
    private async void Save(UserDictDocument doc, string success, Action? before = null, bool deploy = true)
    {
        try
        {
            UserDictWriter.SaveActiveDocument(doc);
        }
        catch (Exception e)
        {
            SetTip(e.Message, Tone.Error);
            return;
        }
        before?.Invoke();
        SetTip(success);
        Reload();
        if (!deploy) return;
        var ok = await Task.Run(() => RimeEngine.Shared.ReloadUserDictionary());
        SetTip(ok ? $"{success}，已部署。" : $"{success}。请到「词库目录」部署生效。");
    }
}

// 编辑词条弹窗：词条变化时自动重填编码；返回 null 表示取消
internal static class EntryEditDialog
{
    public static (string Text, string Code, string Weight, string Note)? Show(Window? owner, UserDictEntry entry)
    {
        var text = UiKit.Field(entry.Text, width: 220);
        var code = UiKit.Field(entry.Code, width: 220, mono: true);
        var weight = UiKit.Field(entry.Weight?.ToString() ?? "", "可空", 220, true);
        var note = UiKit.Field(entry.Note, width: 220);
        text.TextChanged += (_, _) =>
        {
            var encoded = WubiDictEncoder.Encode(text.Text.Trim());
            if (encoded.Length > 0) UiKit.Set(code, encoded);
        };
        var window = new Window
        {
            Title = "编辑词条",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Owner = owner is { IsVisible: true } ? owner : null
        };
        var ok = new Button { Content = "保存", IsDefault = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5) };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var form = UiKit.VStack(8,
            UiKit.Label("编辑词条", 14, FontWeights.SemiBold),
            UiKit.FormRow("词条", 36, text),
            UiKit.FormRow("编码", 36, code),
            UiKit.FormRow("权重", 36, weight),
            UiKit.FormRow("备注", 36, note),
            buttons);
        form.Margin = new Thickness(20, 18, 20, 16);
        window.Content = form;
        window.Loaded += (_, _) => text.Focus();
        if (window.ShowDialog() != true) return null;
        return (text.Text.Trim(), code.Text.Trim().ToLowerInvariant(), weight.Text.Trim(), note.Text.Trim());
    }
}
