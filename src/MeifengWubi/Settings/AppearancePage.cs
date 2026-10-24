using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meifeng.Core.Config;
using MeifengWubi.Candidate;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 外观配置页：顶部固定预览（含测试输入框），下方常规设置与皮肤编辑
internal sealed class AppearancePage : Grid, IDisposable
{
    private static readonly string[] Sample = ["五", "吾", "伍", "捂", "梧", "妩", "坞", "晤", "勿"];
    // 只给部分词条备注，与真实候选一致——没有备注的不占位。
    private static readonly string[] SampleComments = ["数字", "", "姓氏", "", "", "妩媚", "", "会晤", ""];
    private static readonly string[] FullCodes = ["gghg", "ggg", "wgg", "rgkg", "sgkg", "vntg", "fggy", "jgkg", "qre"];
    private static readonly string[] RestCodes = ["hg", "g", "gg", "kg", "g", "tg", "gy", "kg", "re"];

    private readonly Border _previewArea = new() { CornerRadius = new CornerRadius(8), MinHeight = 72, MinWidth = 264 };
    private readonly SkinEditor _editor = new();
    private bool _previewDark = SkinMedia.SystemIsDark;
    private bool _pending;

    public AppearancePage()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var preview = BuildPreviewHost();
        preview.Margin = new Thickness(20, 14, 24, 0);
        Children.Add(preview);

        var form = UiKit.VStack(10, RegularCard(), _editor);
        form.Margin = new Thickness(20, 10, 24, 20);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetRow(scroll, 1);
        Children.Add(scroll);

        AppearancePrefs.Changed += OnPrefsChanged;
        BehaviorPrefs.Changed += OnPrefsChanged;
        ReloadPreview();
    }

    public void Dispose()
    {
        AppearancePrefs.Changed -= OnPrefsChanged;
        BehaviorPrefs.Changed -= OnPrefsChanged;
    }

    // 偏好可能在后台线程变化；合并到一次界面刷新
    private void OnPrefsChanged()
    {
        if (_pending) return;
        _pending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _pending = false;
            ReloadPreview();
            _editor.RefreshThemeSelection();
        });
    }

    // 左侧候选预览，右侧测试输入框；亮暗切换压在左上角；底部分隔线
    private FrameworkElement BuildPreviewHost()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11) });

        _previewArea.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        grid.Children.Add(_previewArea);

        var test = UiKit.Field(placeholder: "在此打字，查看实际候选窗");
        test.FontFamily = new FontFamily("Consolas, Monaco, Global Monospace");
        test.FontSize = 14;
        test.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(test, 2);
        grid.Children.Add(test);

        var appearance = UiKit.Segmented(["亮色", "暗色"], _previewDark ? 1 : 0, i =>
        {
            _previewDark = i == 1;
            ReloadPreview();
        });
        appearance.HorizontalAlignment = HorizontalAlignment.Left;
        appearance.VerticalAlignment = VerticalAlignment.Top;
        foreach (var child in appearance.Children.OfType<Control>())
        {
            child.FontSize = 11;
            child.Padding = new Thickness(8, 1, 8, 2);
        }
        grid.Children.Add(appearance);

        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-20, 0, -24, 0) };
        line.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
        SetRow(line, 1);
        SetColumnSpan(line, 3);
        grid.Children.Add(line);
        return grid;
    }

    // 与实际候选窗共用同一套排版，所见即所得
    private void ReloadPreview()
    {
        var skin = AppearancePrefs.Skin;
        var colors = skin.ColorsFor(_previewDark);
        var pageSize = BehaviorPrefs.PageSize;
        var options = CandidateBarOptions.Current;
        if (options.DisplayMode == CandidateDisplayMode.Hidden)
        {
            var hint = UiKit.Label("候选框已隐藏，仅显示编码", 12, secondary: true);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            _previewArea.Child = hint;
            _previewArea.Height = 72;
            return;
        }
        var codes = (AppearancePrefs.CodeDetail == CodeDetail.Full ? FullCodes : RestCodes).Take(pageSize).ToList();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var comments = AppearancePrefs.ShowCommentInPanel
            ? SampleComments.Take(pageSize).ToList()
            : [];
        var result = CandidateBarRenderer.Build(skin, colors, Sample.Take(pageSize).ToList(), codes, comments, options, true, dpi);
        result.Root.HorizontalAlignment = HorizontalAlignment.Center;
        result.Root.VerticalAlignment = VerticalAlignment.Center;
        result.Root.Width = result.Size.Width;
        result.Root.Height = result.Size.Height;
        result.Root.Margin = new Thickness(8);
        _previewArea.Child = new Viewbox { Child = result.Root, Stretch = Stretch.None, StretchDirection = StretchDirection.DownOnly };
        _previewArea.Height = Math.Max(72, Math.Ceiling(result.Size.Height + 16));
    }

    // 常规：两列均分的六行设置
    private UIElement RegularCard()
    {
        var page = new NumberStepper(() => BehaviorPrefs.PageSize, v => BehaviorPrefs.PageSize = (int)v, 3, 9, 1, compact: false);
        var fonts = SkinMedia.PreferredFonts;
        var fontIndex = Math.Max(0, Array.FindIndex(fonts, f => f.Name == AppearancePrefs.Skin.FontName));
        var font = UiKit.Combo(fonts.Select(f => f.Title), fontIndex, i => ChangeFont(fonts[i].Name));

        var orientation = UiKit.Segmented(Enum.GetValues<CandidateOrientation>().Select(o => o.Title()).ToList(),
            (int)AppearancePrefs.Orientation, i => AppearancePrefs.Orientation = (CandidateOrientation)i);
        var alignment = UiKit.Segmented(Enum.GetValues<SkinVerticalAlignment>().Select(a => a.Title()).ToList(),
            (int)AppearancePrefs.Skin.VerticalAlignment, i => AppearancePrefs.UpdateSkin(s => s with { VerticalAlignment = (SkinVerticalAlignment)i }));

        var display = UiKit.Segmented(Enum.GetValues<CandidateDisplayMode>().Select(m => m.Title()).ToList(),
            (int)AppearancePrefs.DisplayMode, i => AppearancePrefs.DisplayMode = (CandidateDisplayMode)i,
            "仅圆点：只按候选数量显示小圆点；不显示：只保留编码，数字键等照常选词");
        var indexStyle = UiKit.Segmented(Enum.GetValues<IndexStyle>().Select(s => s.Title()).ToList(),
            (int)AppearancePrefs.IndexStyle, i => AppearancePrefs.IndexStyle = (IndexStyle)i,
            "无：不显示序号；1.：带点；1：不带点");

        var preedit = UiKit.Check("", AppearancePrefs.ShowPreeditInPanel, on => AppearancePrefs.ShowPreeditInPanel = on,
            "在候选词后面显示五笔编码；反查时显示单字的五笔编码");
        var codeDetail = UiKit.Segmented(Enum.GetValues<CodeDetail>().Select(d => d.Title()).ToList(),
            (int)AppearancePrefs.CodeDetail, i => AppearancePrefs.CodeDetail = (CodeDetail)i,
            "补码：只显示还没打出来的字母；完整：显示词条的全部编码");

        var rows = UiKit.VStack(10,
            UiKit.TwoColumns(Row("每页候选数", page), Row("候选字体", font)),
            UiKit.TwoColumns(Row("排列方向", orientation), Row("纵向对齐", alignment)),
            UiKit.TwoColumns(Row("候选显示", display), Row("序号", indexStyle)),
            UiKit.TwoColumns(Row("显示编码", preedit), Row("编码范围", codeDetail)));
        return UiKit.Card("常规", null, rows);
    }

    private static StackPanel Row(string title, UIElement control) => UiKit.FormRow(title, 82, control);

    // 换字体后收敛粗细选项，避免指向当前字体不支持的字重
    private void ChangeFont(string name)
    {
        AppearancePrefs.UpdateSkin(skin =>
        {
            var choices = SkinMedia.AvailableWeights(name);
            var fallback = choices.FirstOrDefault(SkinFontWeight.Regular);
            return skin with
            {
                FontName = name,
                CandidateWeight = choices.Contains(skin.CandidateWeight) ? skin.CandidateWeight : fallback,
                IndexWeight = choices.Contains(skin.IndexWeight) ? skin.IndexWeight : fallback
            };
        });
        _editor.Build();
    }
}
