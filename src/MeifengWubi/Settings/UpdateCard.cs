using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Meifeng.Core.Update;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 「版本与更新」卡片：查清单、下载安装包、交给 Inno Setup 静默安装。
// 自动检查在 App 启动时另做一次，这里只负责界面和手动检查。
internal sealed class UpdateCard
{
    // 最近一次创建的卡片；托盘入口用它触发检查，用弱引用避免窗口重开后留旧控件
    private static WeakReference<UpdateCard>? _current;

    private readonly TextBlock _status = UiKit.Label("", 12, secondary: true, wrap: true);
    private readonly Button _check = UiKit.Button("检查更新", () => { });
    private readonly Button _install = UiKit.Button("下载并安装", () => { });
    private readonly Button _skip = UiKit.TextButton("以后再说", () => { });
    private readonly ProgressBar _bar = new()
    {
        Minimum = 0,
        Maximum = 1,
        Height = 4,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 8, 0, 0)
    };
    private readonly TextBox _notes = new()
    {
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = System.Windows.Media.Brushes.Transparent,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        MaxHeight = 150,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 8, 0, 0)
    };

    private UpdateManifest? _manifest;
    private bool _busy;

    public UpdateCard()
    {
        _status.MaxWidth = 460;
        _status.Margin = new Thickness(0, 8, 0, 0);
        _check.Click += (_, _) => _ = CheckAsync(manual: true);
        _install.Click += (_, _) => _ = InstallAsync();
        _skip.Click += (_, _) => SkipCurrent();
        _current = new WeakReference<UpdateCard>(this);
    }

    private static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    // 托盘菜单打开帮助页后调用，直接开始检查
    public static void RequestCheck()
    {
        if (_current?.TryGetTarget(out var card) == true) _ = card.CheckAsync(manual: true);
    }

    public FrameworkElement Build()
    {
        _install.Visibility = Visibility.Collapsed;
        _skip.Visibility = Visibility.Collapsed;
        var auto = UiKit.Check("启动时自动检查", UpdateChecker.AutoCheck, on => UpdateChecker.AutoCheck = on);
        var actions = UiKit.HStack(8, _check, _install, _skip);
        var version = UiKit.Label($"当前版本 {SettingsWindow.VersionText}", 12, secondary: true);
        var content = UiKit.VStack(6, version, actions, _status, _bar, _notes);
        var card = UiKit.Card("版本与更新",
            "从 GitHub 的固定地址读取更新清单；下载后校验 SHA-256 再交给安装程序。", content);
        // 自动检查开关放在卡片底部，和版本信息、按钮分开
        auto.Margin = new Thickness(0, 10, 0, 0);
        if (card.Child is StackPanel stack) stack.Children.Add(auto);
        return card;
    }

    private async Task CheckAsync(bool manual)
    {
        if (_busy) return;
        _busy = true;
        SetButtons(enabled: false);
        UiKit.SetTone(_status, "正在检查更新…", Tone.Idle);
        try
        {
            var result = await UpdateChecker.CheckAsync(CurrentVersion);
            switch (result.Status)
            {
                case UpdateCheckStatus.NewVersion when result.Manifest != null:
                    ShowUpdate(result.Manifest);
                    break;
                case UpdateCheckStatus.UpToDate:
                    ClearUpdate();
                    UiKit.SetTone(_status, $"已是最新版本（{SettingsWindow.VersionText}）。", Tone.Success);
                    break;
                default:
                    UiKit.SetTone(_status, $"检查更新失败：{result.Error}", Tone.Error);
                    break;
            }
        }
        catch (Exception e)
        {
            UiKit.SetTone(_status, $"检查更新失败：{e.Message}", Tone.Error);
        }
        finally
        {
            _busy = false;
            SetButtons(enabled: true);
        }
    }

    private void ShowUpdate(UpdateManifest manifest)
    {
        _manifest = manifest;
        var mandatory = manifest.IsMandatoryFor(UpdateChecker.Format(CurrentVersion));
        var tone = mandatory ? Tone.Error : Tone.Attention;
        var prefix = mandatory ? "需要更新到" : "发现新版本";
        UiKit.SetTone(_status, $"{prefix} v{manifest.Version}（当前 {SettingsWindow.VersionText}）。", tone);

        _notes.Text = string.IsNullOrWhiteSpace(manifest.Notes) ? "（这个版本没有写更新说明）" : manifest.Notes.Trim();
        _notes.Visibility = Visibility.Visible;
        _install.Visibility = Visibility.Visible;
        _skip.Visibility = mandatory ? Visibility.Collapsed : Visibility.Visible;
    }

    // 已是最新时把上一轮遗留的按钮和说明收起来
    private void ClearUpdate()
    {
        _manifest = null;
        _notes.Visibility = Visibility.Collapsed;
        _install.Visibility = Visibility.Collapsed;
        _skip.Visibility = Visibility.Collapsed;
        _bar.Visibility = Visibility.Collapsed;
    }

    private void SkipCurrent()
    {
        if (_manifest == null) return;
        UpdateChecker.SkippedVersion = _manifest.Version;
        _install.Visibility = Visibility.Collapsed;
        _skip.Visibility = Visibility.Collapsed;
        UiKit.SetTone(_status, $"已跳过 v{_manifest.Version}，下次手动检查仍能看到。", Tone.Idle);
    }

    private async Task InstallAsync()
    {
        if (_manifest == null || _busy) return;
        var confirm = MessageBox.Show(
            "更新时玫枫五笔会先退出，安装程序需要管理员权限。\n更新完成后会自动重新启动，正在使用输入法的程序可能需要注销一次。\n\n继续下载并安装吗？",
            "安装更新",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        _busy = true;
        _install.IsEnabled = false;
        _skip.IsEnabled = false;
        _bar.Value = 0;
        _bar.Visibility = Visibility.Visible;
        UiKit.SetTone(_status, "正在下载安装包…", Tone.Idle);
        try
        {
            // Progress 在 UI 线程创建，回调会自动回到 UI 线程
            var progress = new Progress<double>(v =>
            {
                _bar.Value = v;
                UiKit.SetTone(_status, $"正在下载安装包 {v:P0}…", Tone.Idle);
            });
            var file = await UpdateInstaller.DownloadAsync(_manifest, progress);
            _bar.Visibility = Visibility.Collapsed;
            UiKit.SetTone(_status, "下载完成，正在启动安装程序…", Tone.Success);

            if (!UpdateInstaller.TryLaunchInstaller(file))
            {
                UiKit.SetTone(_status, "无法启动安装程序，请手动下载安装包。", Tone.Error);
                _install.IsEnabled = true;
                _skip.IsEnabled = true;
                _busy = false;
                return;
            }
            // 安装器会结束本进程并替换文件
            App.QuitForUpdate();
        }
        catch (Exception e)
        {
            _bar.Visibility = Visibility.Collapsed;
            UiKit.SetTone(_status, $"更新失败：{e.Message}", Tone.Error);
            _install.IsEnabled = true;
            _skip.IsEnabled = true;
            _busy = false;
        }
    }

    private void SetButtons(bool enabled)
    {
        _check.IsEnabled = enabled;
        if (_manifest != null)
        {
            _install.IsEnabled = enabled;
            _skip.IsEnabled = enabled;
        }
    }
}
