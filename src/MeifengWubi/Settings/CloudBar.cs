using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meifeng.Core.Engine;
using Meifeng.Core.Sync;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 用户词页顶部一条（对应 macOS UserLexiconCloudBar）：用户词同步和扩展词库的状态与按钮
internal sealed class CloudBar : Border, IDisposable
{
    private const string SyncIdleLoggedOut = "请先在「账户」页登录";
    private const string SyncIdleLoggedIn = "先对比，再合并或覆盖";
    private const string SyncIdleAuto = "自动同步：启动时、改词约 3 秒后、每 10 分钟；词库有变化就重新加载";
    private const string ExtraIdle = "获取后覆盖本地";

    private readonly TextBlock _accountMeta = Meta();
    private readonly TextBlock _extraMeta = Meta();
    private readonly CheckBox _autoSync;
    private readonly Button _compare, _merge, _upload, _download, _extra;
    private readonly TextBlock _syncStatus = Status();
    private readonly TextBlock _extraStatus = Status();
    private bool _busy;
    private string? _remoteYaml;
    private Meifeng.Core.Lexicon.UserDictDocument? _remoteDocument;

    // 写回本地用户词后通知页面刷新
    public event Action? UserDictChanged;

    public CloudBar()
    {
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 8, 12, 8);
        SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");

        _autoSync = UiKit.Check("自动同步", UserDictAutoSync.IsEnabled, AutoSyncChanged,
            "打开后：输入法启动时同步一次；本地用户词改动约 3 秒后同步；运行中每 10 分钟同步一次。本地词库比上次加载新时会重新加载，正在输入则等这次打完。两边都改过的同一条不会自动覆盖。");
        _autoSync.FontSize = 12;
        _compare = Small("对比差异", Compare, "拉取云端用户词，只对比不改词库");
        _merge = Small("增量合并", Merge, "保留本地词条，并入云端多出的词");
        _upload = Small("本地 → 云端", Upload, "用本地用户词覆盖云端");
        _download = Small("本地 ← 云端", Download, "用云端用户词覆盖本地");
        _extra = Small("获取扩展词库", PullExtra, "用线上扩展词库覆盖本地文件，不在此查看或编辑");

        var divider = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
        divider.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
        var stack = new StackPanel();
        stack.Children.Add(MakeRow("\uE895", "用户词同步", _accountMeta, [_autoSync, _compare, _merge, _upload, _download], _syncStatus));
        stack.Children.Add(divider);
        stack.Children.Add(MakeRow("\uE896", "扩展词库", _extraMeta, [_extra], _extraStatus));
        Child = stack;

        UserDictAutoSync.DidFinish += OnAutoSyncFinished;
        WubiCloudConfig.Changed += OnAccountChanged;
        Refresh();
    }

    public void Dispose()
    {
        UserDictAutoSync.DidFinish -= OnAutoSyncFinished;
        WubiCloudConfig.Changed -= OnAccountChanged;
    }

    private void OnAutoSyncFinished() => Dispatcher.BeginInvoke(ShowStoredAutoSyncResult);

    private void OnAccountChanged() => Dispatcher.BeginInvoke(Refresh);

    private static TextBlock Meta()
    {
        var t = UiKit.Label("", 11, secondary: true);
        t.MaxWidth = 96;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    private static TextBlock Status()
    {
        var t = UiKit.Label("", 11, secondary: true, wrap: true);
        t.MaxHeight = 34;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.Margin = new Thickness(0, 4, 0, 0);
        return t;
    }

    private static Button Small(string title, Action click, string tip)
    {
        var b = UiKit.Button(title, click, tip: tip);
        b.Margin = new Thickness(0, 0, 6, 0);
        return b;
    }

    // 按钮占上面一行，说明独占下面整行
    private static UIElement MakeRow(string glyph, string title, TextBlock meta, IReadOnlyList<FrameworkElement> controls, TextBlock status)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentFillColorDefaultBrush");
        var titleLabel = UiKit.Label(title, 12, FontWeights.SemiBold);
        titleLabel.Width = 72;
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        icon.Margin = new Thickness(0, 0, 8, 0);
        titleLabel.Margin = new Thickness(0, 0, 8, 0);
        meta.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        row.Children.Add(titleLabel);
        row.Children.Add(meta);
        foreach (var c in controls)
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            if (c is CheckBox) c.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(c);
        }
        var column = new StackPanel();
        column.Children.Add(row);
        column.Children.Add(status);
        return column;
    }

    // 切到用户词页或窗口再次显示时调用
    public void Refresh()
    {
        var loggedIn = WubiCloudConfig.IsLoggedIn;
        _accountMeta.Text = WubiCloudConfig.UserInfo is { IsLoggedIn: true } user
            ? (user.Nickname.Length == 0 ? user.Email : user.Nickname)
            : "未登录";
        _extraMeta.Text = $"{ExtraDictUpdater.LocalEntryCount()} 条";
        var enabled = loggedIn && !_busy;
        foreach (var b in new[] { _compare, _merge, _upload, _download, _extra }) b.IsEnabled = enabled;
        _autoSync.IsChecked = UserDictAutoSync.IsEnabled;
        if (!loggedIn)
        {
            SetStatus(_syncStatus, SyncIdleLoggedOut);
        }
        else if (IsIdle(_syncStatus.Text))
        {
            if (UserDictAutoSync.IsEnabled && UserDictAutoSync.Last != null) ShowStoredAutoSyncResult();
            else SetStatus(_syncStatus, UserDictAutoSync.IsEnabled ? SyncIdleAuto : SyncIdleLoggedIn);
        }
        if (_extraStatus.Text.Length == 0 || _extraStatus.Text == ExtraIdle) SetStatus(_extraStatus, ExtraIdle);
    }

    private void AutoSyncChanged(bool on)
    {
        UserDictAutoSync.Install();
        UserDictAutoSync.IsEnabled = on;
        if (on)
        {
            var loggedIn = WubiCloudConfig.IsLoggedIn;
            SetStatus(_syncStatus, loggedIn ? "自动同步已打开" : "自动同步已打开，登录后才会开始", loggedIn ? Tone.Success : Tone.Idle);
        }
        else
        {
            SetStatus(_syncStatus, "自动同步已关闭");
        }
    }

    // 别盖住刚做完的手动同步结果；只刷新空闲说明和上一轮自动同步
    private static bool IsIdle(string text) =>
        text.Length == 0 || text is SyncIdleLoggedOut or SyncIdleLoggedIn or SyncIdleAuto;

    private void ShowStoredAutoSyncResult()
    {
        if (!(IsIdle(_syncStatus.Text) || _syncStatus.Text.StartsWith("自动同步", StringComparison.Ordinal))) return;
        if (UserDictAutoSync.Last is not { } last) return;
        var tone = last.Tone switch
        {
            UserDictAutoSync.Tone.Error => Tone.Error,
            UserDictAutoSync.Tone.Attention => Tone.Attention,
            _ => Tone.Success
        };
        SetStatus(_syncStatus, last.Text, tone);
        UserDictChanged?.Invoke();
    }

    private static void SetStatus(TextBlock label, string text, Tone tone = Tone.Idle) => UiKit.SetTone(label, text, tone);

    private async void Compare()
    {
        var outcome = await Run("对比中…", _syncStatus, CloudDictSync.CompareAsync);
        if (outcome == null) return;
        _remoteYaml = outcome.RemoteYaml;
        _remoteDocument = outcome.RemoteDocument;
        SetStatus(_syncStatus, $"对比完成：{outcome.Summary}", Tone.Success);
        // 差异放进可滚动弹窗，并在这里选择合并或覆盖
        var steps = new List<(string Title, Action Run)>();
        if (!outcome.Diff.HasRemote) steps.Add(("上传到云端", Upload));
        else if (!outcome.Diff.IsIdentical)
        {
            steps.Add(("增量合并", Merge));
            steps.Add(("本地 → 云端", Upload));
            steps.Add(("本地 ← 云端", Download));
        }
        var choice = DiffDialog.Show(Window.GetWindow(this), outcome.Summary, outcome.Diff.DetailReport(), steps.Select(s => s.Title).ToList());
        if (choice >= 0) steps[choice].Run();
    }

    private async void Merge()
    {
        var remote = _remoteDocument;
        if (await Run("合并中…", _syncStatus, () => CloudDictSync.MergeAsync(remote)) != null) await FinishWrite(_syncStatus, true);
    }

    private async void Upload()
    {
        await Run("上传中…", _syncStatus, CloudDictSync.UploadLocalAsync);
    }

    private async void Download()
    {
        var cached = _remoteYaml;
        if (await Run("下载中…", _syncStatus, () => CloudDictSync.DownloadAsync(cached)) != null) await FinishWrite(_syncStatus, true);
    }

    private async void PullExtra()
    {
        var text = await Run("正在获取扩展词库…", _extraStatus, async () =>
        {
            var (added, count) = await CloudDictSync.PullExtraAsync();
            return added == 0 ? $"获取完成：没有新增，扩展词库共 {count} 条" : $"获取完成：新增 {added} 条，扩展词库共 {count} 条";
        });
        if (text == null) return;
        await FinishWrite(_extraStatus, false);
        _extraMeta.Text = $"{ExtraDictUpdater.LocalEntryCount()} 条";
    }

    // 写回本地后重新加载词库；失败改成红色，避免看起来像已经生效
    private async Task FinishWrite(TextBlock target, bool userDictChanged)
    {
        var ok = await Task.Run(() => RimeEngine.Shared.ReloadUserDictionary());
        var message = ok ? "已重新加载词库" : "词库重新加载失败，请在「词库目录」页部署";
        var text = target.Text.Length == 0 ? message : $"{target.Text}。{message}";
        SetStatus(target, text, ok ? Tone.Success : Tone.Error);
        if (userDictChanged) UserDictChanged?.Invoke();
    }

    // 后台执行网络操作，期间禁用按钮；返回字符串时直接作为成功提示
    private async Task<T?> Run<T>(string pending, TextBlock label, Func<Task<T>> work) where T : class
    {
        if (_busy) return null;
        _busy = true;
        SetStatus(label, pending);
        Refresh();
        try
        {
            var value = await Task.Run(work);
            if (value is string s) SetStatus(label, s, Tone.Success);
            return value;
        }
        catch (Exception e)
        {
            SetStatus(label, e.Message, Tone.Error);
            return null;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }
}

// 用户词差异弹窗：摘要、可滚动明细和下一步按钮；返回所选按钮序号，关闭为 -1
internal static class DiffDialog
{
    public static int Show(Window? owner, string summary, string report, IReadOnlyList<string> steps)
    {
        var result = -1;
        var window = new Window
        {
            Title = "用户词差异",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Owner = owner is { IsVisible: true } ? owner : null
        };
        var stack = new StackPanel { Margin = new Thickness(20, 18, 20, 16), Width = 560 };
        stack.Children.Add(UiKit.Label("用户词差异", 14, FontWeights.SemiBold));
        var sub = UiKit.Label(summary, 12, wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(sub);
        if (report.Length > 0)
        {
            var lines = report.Split('\n').Length;
            var box = new TextBox
            {
                Text = report,
                IsReadOnly = true,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = Math.Min(260, Math.Max(64, lines * 18 + 20)),
                Margin = new Thickness(0, 12, 0, 0)
            };
            stack.Children.Add(box);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        for (var i = 0; i < steps.Count; i++)
        {
            var index = i;
            var b = new Button { Content = steps[i], MinWidth = 88, Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            if (i == 0) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
            b.Click += (_, _) =>
            {
                result = index;
                window.Close();
            };
            buttons.Children.Add(b);
        }
        var close = new Button { Content = "关闭", IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 4, 12, 5) };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(close);
        stack.Children.Add(buttons);
        window.Content = stack;
        window.ShowDialog();
        return result;
    }
}
