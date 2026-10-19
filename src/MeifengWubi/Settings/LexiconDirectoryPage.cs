using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meifeng.Core.Config;
using Meifeng.Core.Lexicon;
using MeifengWubi.Ui;
using Microsoft.Win32;

namespace MeifengWubi.Settings;

// 词库目录页（对应 macOS LexiconDirectorySettingsView + CharMapSettingsView）：路径切换、部署与复位、单字字典
internal sealed class LexiconDirectoryPage : ContentControl
{
    private readonly TextBlock _caption = UiKit.Label("", 11, FontWeights.Medium, secondary: true);
    private readonly TextBox _path = UiKit.SelectableText("", 11, mono: true);
    private readonly TextBlock _userFile = UiKit.Label("", 11, secondary: true);
    private readonly Button _useDefault;
    private readonly Button _useRime;
    private readonly TextBlock _banner = UiKit.Label("", 11, wrap: true);
    private readonly ComboBox _dict = new() { Width = 280, FontSize = 12 };
    private readonly TextBlock _dictStatus = UiKit.Label("", 11, secondary: true, wrap: true);
    private bool _busy, _fillingDicts;

    public LexiconDirectoryPage()
    {
        _useDefault = UiKit.Button("切回默认目录", () => ChangeDir(null));
        _useRime = UiKit.Button("切到 Rime 目录", () => ChangeDir(AppPaths.WeaselRimeUserDataDir));
        _banner.Visibility = Visibility.Collapsed;
        _banner.MaxWidth = 360;

        var pathButtons = UiKit.HStack(8,
            UiKit.Button("复制路径", CopyPath),
            UiKit.Button("在资源管理器中显示", Reveal),
            UiKit.Button("更改目录…", ChooseDir));
        var switchButtons = UiKit.HStack(8, _useDefault, _useRime);
        var deployRow = UiKit.HStack(8, UiKit.Button("部署当前配置", Deploy), UiKit.Button("复位原始配置", Reset), _banner);
        var hint = UiKit.Label($"「切到 Rime 目录」使用 {AppPaths.WeaselRimeUserDataDir}，与小狼毫等前端相同。「切回默认目录」回到本输入法自己的目录。已有方案的目录不会被改写，空目录才会补上内置五笔。", 11, secondary: true, wrap: true);
        hint.MaxWidth = 560;
        hint.HorizontalAlignment = HorizontalAlignment.Left;
        _path.MaxWidth = 560;
        _path.HorizontalAlignment = HorizontalAlignment.Left;
        var directory = new StackPanel();
        directory.Children.Add(_caption);
        directory.Children.Add(Spaced(_path, 6));
        directory.Children.Add(Spaced(_userFile, 6));
        directory.Children.Add(Spaced(pathButtons, 10));
        directory.Children.Add(Spaced(switchButtons, 8));
        directory.Children.Add(Spaced(deployRow, 10));
        directory.Children.Add(Spaced(hint, 12));

        _dict.SelectionChanged += (_, _) => DictSelectionChanged();
        var charMap = UiKit.VStack(8, UiKit.HStack(8, _dict, UiKit.Button("生成单字字典", GenerateCharMap)), _dictStatus);
        _dictStatus.MaxWidth = 480;
        _dictStatus.HorizontalAlignment = HorizontalAlignment.Left;

        var column = UiKit.VStack(16,
            UiKit.Card("目录与部署", null, directory),
            UiKit.Card("单字字典", "从参考码表提取单字编码，添加词条和快捷造词时用来自动生成词组编码。", charMap));
        Content = UiKit.Page("词库目录", "管理 Rime 用户数据目录，并维护自动编码用的单字字典。", column);
        RefreshDirectoryInfo();
        ReloadDicts();
    }

    private static FrameworkElement Spaced(FrameworkElement e, double top)
    {
        e.Margin = new Thickness(0, top, 0, 0);
        return e;
    }

    private Window? Owner => Window.GetWindow(this);

    private void RefreshDirectoryInfo()
    {
        var current = RimeResourceDeployer.ResolveUserDataDir();
        var onDefault = AppPaths.CustomRimeUserDataDir == null;
        var onRime = AppPaths.SamePath(current, AppPaths.WeaselRimeUserDataDir);
        _caption.Text = onDefault ? "词库目录（输入法默认）：" : onRime ? "词库目录（Rime）：" : "词库目录（自定义）：";
        _path.Text = current;
        _userFile.Text = $"用户词库文件：{Path.GetFileName(AppPaths.UserDictPath)}";
        _useDefault.IsEnabled = !onDefault;
        _useRime.IsEnabled = !onRime;
        _useDefault.ToolTip = AppPaths.DefaultRimeUserDataDir;
        _useRime.ToolTip = AppPaths.WeaselRimeUserDataDir;
    }

    private void ShowBanner(bool success, string text)
    {
        _banner.Visibility = Visibility.Visible;
        UiKit.SetTone(_banner, (success ? "✓ " : "✕ ") + text, success ? Tone.Success : Tone.Error);
    }

    private void CopyPath()
    {
        try
        {
            Clipboard.SetText(RimeResourceDeployer.ResolveUserDataDir());
            ShowBanner(true, "已复制词库目录路径");
        }
        catch (Exception e)
        {
            ShowBanner(false, e.Message);
        }
    }

    private static void Reveal()
    {
        var dir = RimeResourceDeployer.ResolveUserDataDir();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void ChooseDir()
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog
        {
            Title = "选择已有的 Rime 配置目录。目录里已有方案时保持原样。",
            InitialDirectory = RimeResourceDeployer.ResolveUserDataDir()
        };
        if (dialog.ShowDialog(Owner) != true) return;
        ChangeDir(dialog.FolderName);
    }

    // 输入法与设置在同一进程，切换后引擎已经改用新目录，无需重启
    private async void ChangeDir(string? dir)
    {
        if (_busy) return;
        _busy = true;
        var oldPath = RimeResourceDeployer.ResolveUserDataDir();
        ShowBanner(true, "正在切换并部署…");
        var (ok, message) = await LexiconActions.ChangeDirectoryAsync(dir);
        RefreshDirectoryInfo();
        if (ok && !AppPaths.SamePath(oldPath, RimeResourceDeployer.ResolveUserDataDir()))
        {
            WubiDictEncoder.ClearCache();
            ReloadDicts();
        }
        ShowBanner(ok, message);
        _busy = false;
    }

    private async void Deploy()
    {
        if (_busy) return;
        _busy = true;
        ShowBanner(true, "正在部署…");
        var (ok, message) = await LexiconActions.DeployAsync();
        ShowBanner(ok, message);
        _busy = false;
    }

    private async void Reset()
    {
        if (_busy) return;
        if (!UiKit.Confirm(Owner, "复位原始配置？", "将清空当前词库目录中的配置并恢复内置文件，然后重新部署。", "复位", destructive: true)) return;
        _busy = true;
        ShowBanner(true, "正在复位…");
        var (ok, message) = await LexiconActions.ResetAsync();
        RefreshDirectoryInfo();
        ShowBanner(ok, message);
        _busy = false;
    }

    private void ReloadDicts()
    {
        var names = WubiDictEncoder.AvailableDictFileNames();
        _fillingDicts = true;
        _dict.Items.Clear();
        if (names.Count == 0)
        {
            _dict.Items.Add("未找到码表");
            _dict.SelectedIndex = 0;
            _dict.IsEnabled = false;
            _fillingDicts = false;
            UiKit.SetTone(_dictStatus, "词库目录里没有可用的 .dict.yaml", Tone.Attention);
            return;
        }
        _dict.IsEnabled = true;
        foreach (var n in names) _dict.Items.Add(n);
        var selected = WubiDictEncoder.SelectedMainDictName;
        if (names.Contains(selected))
        {
            _dict.SelectedItem = selected;
        }
        else
        {
            _dict.SelectedIndex = 0;
            WubiDictEncoder.SelectedMainDictName = names[0];
            WubiDictEncoder.ClearCache();
        }
        _fillingDicts = false;
        var status = WubiDictEncoder.CharMapStatus();
        if (status.Exists) UiKit.SetTone(_dictStatus, $"已生成 {status.Count} 个单字", Tone.Idle);
        else
        {
            UiKit.SetTone(_dictStatus, "正在从参考码表生成…", Tone.Idle);
            GenerateCharMap();
        }
    }

    private void DictSelectionChanged()
    {
        if (_fillingDicts || _dict.SelectedItem is not string title || !title.EndsWith(".dict.yaml", StringComparison.Ordinal)) return;
        WubiDictEncoder.SelectedMainDictName = title;
        WubiDictEncoder.ClearCache();
        GenerateCharMap();
    }

    private async void GenerateCharMap()
    {
        UiKit.SetTone(_dictStatus, "正在生成…", Tone.Idle);
        try
        {
            var count = await Task.Run(WubiDictEncoder.RebuildCharMap);
            UiKit.SetTone(_dictStatus, $"已生成 {count} 个单字", Tone.Idle);
        }
        catch (Exception e)
        {
            UiKit.SetTone(_dictStatus, e.Message, Tone.Error);
        }
    }
}
