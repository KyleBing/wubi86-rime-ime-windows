using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Meifeng.Core.Config;
using MeifengWubi.Dialogs;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 设置窗口（对应 macOS SettingsRootView）：左侧栏目，右侧缓存的各设置页
internal sealed class SettingsWindow : Window
{
    // 用户词和词库目录放最上面；帮助钉在侧栏底部
    internal enum Section { UserLexicon, LexiconDirectory, Appearance, Keyboard, Punctuation, Account, Help }

    private static SettingsWindow? _current;

    private readonly Dictionary<Section, FrameworkElement> _pages = [];
    private readonly Dictionary<Section, ToggleButton> _buttons = [];
    private readonly Border _content = new();
    private Section _section = Section.UserLexicon;

    // 打开（或前置）设置窗口
    public static void Open(Section? section = null)
    {
        _current ??= new SettingsWindow();
        _current.ShowSection(section ?? _current._section);
        if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Show();
        _current.Activate();
    }

    private SettingsWindow()
    {
        Title = "玫枫五笔设置";
        Width = 900;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = ImeDialog.BrandIcon;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = BuildSidebar();
        var divider = new Border();
        divider.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(_content, 2);
        root.Children.Add(sidebar);
        root.Children.Add(divider);
        root.Children.Add(_content);
        Content = root;
        Closed += (_, _) =>
        {
            foreach (var page in _pages.Values) (page as IDisposable)?.Dispose();
            _current = null;
        };
        // 窗口重新激活时，用户词页按磁盘重读
        Activated += (_, _) =>
        {
            if (_section == Section.UserLexicon && _pages.TryGetValue(Section.UserLexicon, out var page)) (page as UserLexiconPage)?.Reload();
        };
    }

    private static string SectionTitle(Section s) => s switch
    {
        Section.UserLexicon => "用户词",
        Section.LexiconDirectory => "词库目录",
        Section.Appearance => "外观配置",
        Section.Keyboard => "输入习惯",
        Section.Punctuation => "标点",
        Section.Account => "账户",
        _ => "帮助"
    };

    // Segoe Fluent Icons 字形
    private static string Glyph(Section s) => s switch
    {
        Section.UserLexicon => "\uE82D",
        Section.LexiconDirectory => "\uE8B7",
        Section.Appearance => "\uE790",
        Section.Keyboard => "\uE765",
        Section.Punctuation => "\uE8D2",
        Section.Account => "\uE77B",
        _ => "\uE897"
    };

    public static string VersionText
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "v-" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private FrameworkElement BuildSidebar()
    {
        var dock = new DockPanel { Margin = new Thickness(16, 22, 16, 16), LastChildFill = false };

        var logo = new Image { Width = 44, Height = 44 };
        try
        {
            var path = Path.Combine(AppPaths.ResourcesDir, "Brand", "SettingsLogo.png");
            if (File.Exists(path)) logo.Source = new BitmapImage(new Uri(path));
        }
        catch
        {
            // 没有图标时只显示文字
        }
        var names = UiKit.VStack(1, UiKit.Label("玫枫五笔", 16, FontWeights.SemiBold), UiKit.Label("输入法设置", 11, secondary: true));
        var brand = UiKit.HStack(10, logo, names);
        DockPanel.SetDock(brand, Dock.Top);
        dock.Children.Add(brand);

        var nav = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        foreach (var s in Enum.GetValues<Section>().Where(s => s != Section.Help)) nav.Children.Add(SectionButton(s));
        DockPanel.SetDock(nav, Dock.Top);
        dock.Children.Add(nav);

        var version = UiKit.SelectableText(VersionText, 10);
        version.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");
        DockPanel.SetDock(version, Dock.Bottom);
        dock.Children.Add(version);
        var hint = UiKit.Label("本地设置自动保存", 10);
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        hint.Margin = new Thickness(0, 8, 0, 2);
        DockPanel.SetDock(hint, Dock.Bottom);
        dock.Children.Add(hint);
        var help = SectionButton(Section.Help);
        DockPanel.SetDock(help, Dock.Bottom);
        dock.Children.Add(help);
        return dock;
    }

    private ToggleButton SectionButton(Section s)
    {
        var icon = new TextBlock { Text = Glyph(s), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 22 };
        var title = UiKit.Label(SectionTitle(s), 13, FontWeights.Medium);
        var b = new ToggleButton
        {
            Content = UiKit.HStack(6, icon, title),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 34,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        b.Click += (_, _) => ShowSection(s);
        _buttons[s] = b;
        return b;
    }

    private void ShowSection(Section s)
    {
        _section = s;
        foreach (var (key, b) in _buttons) b.IsChecked = key == s;
        if (!_pages.TryGetValue(s, out var page))
        {
            page = s switch
            {
                Section.UserLexicon => new UserLexiconPage(),
                Section.LexiconDirectory => new LexiconDirectoryPage(),
                Section.Appearance => new AppearancePage(),
                Section.Keyboard => KeyboardPage.Build(),
                Section.Punctuation => PunctuationPage.Build(),
                Section.Account => AccountPage.Build(),
                _ => HelpPage.Build()
            };
            _pages[s] = page;
        }
        _content.Child = page;
        if (page is UserLexiconPage lexicon) lexicon.Reload();
    }
}
