using System.Windows;
using System.Windows.Controls;
using Meifeng.Core.Config;
using MeifengWubi.Ui;
using Microsoft.Win32;

namespace MeifengWubi.Settings;

// 外观页的皮肤编辑区（对应 macOS CandidateSkinSettingsView）：布局、配色与分享，修改立即写入偏好
internal sealed class SkinEditor : StackPanel
{
    private readonly List<ThemeCell> _themeCells = [];
    private readonly TextBlock _themeStatus = UiKit.Label("", 11, secondary: true);
    private readonly TextBlock _shareStatus = UiKit.Label("", 11, secondary: true, wrap: true);

    public SkinEditor()
    {
        Build();
    }

    private static CandidateSkin Skin => AppearancePrefs.Skin;

    private static void Update(Func<CandidateSkin, CandidateSkin> change) => AppearancePrefs.UpdateSkin(change);

    // 主题、导入、恢复默认后整块重建，让所有输入框显示新值
    public void Build()
    {
        Children.Clear();
        _themeCells.Clear();
        // 状态文字跨重建复用，先从旧的父容器摘下
        (_themeStatus.Parent as Panel)?.Children.Remove(_themeStatus);
        (_shareStatus.Parent as Panel)?.Children.Remove(_shareStatus);
        var skin = Skin;

        var cards = new List<UIElement>
        {
            Card("主题", ThemeForm()),
            Card("边距与位置", new SpacingDiagram(
                Compact("上", () => Skin.PaddingTop, v => Update(s => s with { PaddingTop = v }), 0, 30),
                Compact("下", () => Skin.PaddingBottom, v => Update(s => s with { PaddingBottom = v }), 0, 30),
                Compact("左", () => Skin.PaddingLeft, v => Update(s => s with { PaddingLeft = v }), 0, 40),
                Compact("右", () => Skin.PaddingRight, v => Update(s => s with { PaddingRight = v }), 0, 40),
                Compact("距光标上", () => Skin.OffsetTop, v => Update(s => s with { OffsetTop = v }), -40, 80),
                Compact("距光标左", () => Skin.OffsetLeft, v => Update(s => s with { OffsetLeft = v }), -80, 80),
                Compact("圆角", () => Skin.CornerRadius, v => Update(s => s with { CornerRadius = v }), 0, 30))),
            Card("布局细节", LayoutForm(skin)),
            Card("明暗配色", ColorColumns(
            [
                ("背景", 0), ("候选词", 1), ("序号", 2), ("编码", 8), ("边框", 3), ("阴影", 4)
            ])),
            Card("边框与阴影", ShadowForm(skin)),
            Card("首选候选", UiKit.TwoColumns(HighlightForm(skin), ColorColumns(
            [
                ("高亮背景", 5), ("首选文字", 6), ("首选序号", 7), ("首选编码", 9)
            ]), 12)),
            Card("皮肤信息与分享", ShareForm(skin))
        };
        UiKit.AddSpaced(this, 10, cards);
        RefreshThemeSelection();
    }

    // 外观页卡片：标题 + 内容
    private static Border Card(string title, UIElement content) => UiKit.Card(title, null, content);

    // 主题：内置主题一行，下方 8 个存储位（2 行 × 4 列）；点击卡片即应用
    private UIElement ThemeForm()
    {
        var presets = CandidateSkin.Presets.Select((preset, i) =>
        {
            var cell = new ThemeCell(preset, preset.Name, "内置", false) { OnApply = () => ApplyPreset(i) };
            _themeCells.Add(cell);
            return (UIElement)cell;
        }).ToList();
        var slots = AppearancePrefs.SkinSlots.Select((saved, i) =>
        {
            var title = saved == null ? "空位" : saved.Name.Length == 0 ? "未命名" : saved.Name;
            var cell = new ThemeCell(saved, title, $"{i + 1}", true)
            {
                OnApply = () => ApplySlot(i),
                OnSave = () => SaveSlot(i),
                OnClear = () => ClearSlot(i)
            };
            _themeCells.Add(cell);
            return (UIElement)cell;
        }).ToList();
        _themeStatus.Text = "";
        var hint = UiKit.Label("点击主题即可应用；「存入当前」把正在使用的外观保存到该位置。", 11, secondary: true, wrap: true);
        var presetGrid = UiKit.Grid(presets, 4);
        presetGrid.Margin = new Thickness(0, 0, 0, 4);
        return UiKit.VStack(6, SectionCaption("内置主题"), presetGrid, SectionCaption("我的主题"), UiKit.Grid(slots, 4), hint, _themeStatus);
    }

    private static TextBlock SectionCaption(string text) => UiKit.Label(text, 11, FontWeights.Medium, secondary: true);

    // 与当前皮肤外观一致的卡片加强调色描边
    public void RefreshThemeSelection()
    {
        var current = Skin;
        foreach (var cell in _themeCells) cell.SetCurrent(cell.Skin?.LooksSame(current) ?? false);
    }

    private void ApplyPreset(int index)
    {
        if (index < 0 || index >= CandidateSkin.Presets.Count) return;
        var preset = CandidateSkin.Presets[index];
        AppearancePrefs.ApplyPreset(preset);
        Build();
        _themeStatus.Text = $"已应用内置主题「{preset.Name}」。";
    }

    private void ApplySlot(int index)
    {
        if (AppearancePrefs.SkinSlots[index] is not { } saved) return;
        AppearancePrefs.ApplySkinSlot(index);
        Build();
        _themeStatus.Text = $"已应用位置 {index + 1} 的主题「{saved.Name}」。";
    }

    // 已有内容时确认覆盖，避免误点丢失
    private void SaveSlot(int index)
    {
        if (AppearancePrefs.SkinSlots[index] is { } existing &&
            !UiKit.Confirm(Window.GetWindow(this), $"覆盖位置 {index + 1}？", $"「{existing.Name}」会被当前外观替换。", "覆盖"))
        {
            return;
        }
        AppearancePrefs.SaveCurrentSkin(index);
        Build();
        _themeStatus.Text = $"已保存到位置 {index + 1}。";
    }

    private void ClearSlot(int index)
    {
        if (AppearancePrefs.SkinSlots[index] is not { } existing) return;
        if (!UiKit.Confirm(Window.GetWindow(this), $"清空位置 {index + 1}？", $"「{existing.Name}」将被删除，当前正在使用的外观不受影响。", "清空", destructive: true)) return;
        AppearancePrefs.ClearSkinSlot(index);
        Build();
        _themeStatus.Text = $"已清空位置 {index + 1}。";
    }

    // 示意图用的紧凑控件
    private static NumberStepper Compact(string caption, Func<double> get, Action<double> set, double min, double max) =>
        new(get, set, min, max, 1, compact: true, caption: caption);

    // 表单行：72 宽的右对齐标签 + 控件
    private static StackPanel Row(string title, UIElement control) => UiKit.FormRow(title, 72, control);

    private static StackPanel NumberRow(string title, Func<double> get, Action<double> set, double min = 0, double max = 100, double step = 1) =>
        Row(title, new NumberStepper(get, set, min, max, step, compact: false));

    // 字重下拉：只列当前字体支持的档位
    private static StackPanel WeightRow(string title, string fontName, SkinFontWeight value, Action<SkinFontWeight> changed)
    {
        var choices = SkinMedia.AvailableWeights(fontName);
        var shown = choices.Contains(value) ? value : choices.FirstOrDefault(SkinFontWeight.Regular);
        var combo = UiKit.Combo(choices.Select(w => w.Title()), Math.Max(0, choices.IndexOf(shown)), i => changed(choices[i]), 110);
        return Row(title, combo);
    }

    // 两列均分：间距一对、字号一对，下方粗细一对
    private static UIElement LayoutForm(CandidateSkin skin)
    {
        var spacing = UiKit.VStack(7,
            NumberRow("候选间距", () => Skin.ItemSpacing, v => Update(s => s with { ItemSpacing = v })),
            NumberRow("序号间距", () => Skin.IndexSpacing, v => Update(s => s with { IndexSpacing = v }), 0, 20));
        var sizes = UiKit.VStack(7,
            NumberRow("候选字号", () => Skin.CandidateFontSize, v => Update(s => s with { CandidateFontSize = v })),
            NumberRow("序号字号", () => Skin.IndexFontSize, v => Update(s => s with { IndexFontSize = v })),
            NumberRow("编码字号", () => Skin.CodeFontSize, v => Update(s => s with { CodeFontSize = v }), 8, 32));
        var weights = UiKit.VStack(7,
            WeightRow("候选粗细", skin.FontName, skin.CandidateWeight, w => Update(s => s with { CandidateWeight = w })),
            WeightRow("序号粗细", skin.FontName, skin.IndexWeight, w => Update(s => s with { IndexWeight = w })));
        return UiKit.VStack(8, UiKit.TwoColumns(spacing, sizes, 12), weights);
    }

    private static UIElement ShadowForm(CandidateSkin skin) => UiKit.VStack(7,
        UiKit.Check("自定义阴影", skin.ShadowEnabled, on => Update(s => s with { ShadowEnabled = on })),
        NumberRow("边框宽度", () => Skin.BorderWidth, v => Update(s => s with { BorderWidth = v }), 0, 100, 0.5),
        NumberRow("阴影模糊", () => Skin.ShadowRadius, v => Update(s => s with { ShadowRadius = v })),
        NumberRow("阴影 X", () => Skin.ShadowOffsetX, v => Update(s => s with { ShadowOffsetX = v })),
        NumberRow("阴影 Y", () => Skin.ShadowOffsetY, v => Update(s => s with { ShadowOffsetY = v })));

    // 首选候选开关、圆角、留白与字重
    private static UIElement HighlightForm(CandidateSkin skin)
    {
        var hint = UiKit.Label("首位候选（空格上屏）单独着色；留白对所有候选生效，保证间距与行高一致。", 11, secondary: true, wrap: true);
        hint.MaxWidth = 240;
        hint.HorizontalAlignment = HorizontalAlignment.Left;
        var padding = new HighlightPaddingDiagram(
            Compact("上", () => Skin.HighlightPaddingTop, v => Update(s => s with { HighlightPaddingTop = v }), 0, 20),
            Compact("下", () => Skin.HighlightPaddingBottom, v => Update(s => s with { HighlightPaddingBottom = v }), 0, 20),
            Compact("左", () => Skin.HighlightPaddingLeft, v => Update(s => s with { HighlightPaddingLeft = v }), 0, 20),
            Compact("右", () => Skin.HighlightPaddingRight, v => Update(s => s with { HighlightPaddingRight = v }), 0, 20));
        return UiKit.VStack(7,
            UiKit.Check("首选使用高亮样式", skin.HighlightEnabled, on => Update(s => s with { HighlightEnabled = on })),
            NumberRow("高亮圆角", () => Skin.HighlightCornerRadius, v => Update(s => s with { HighlightCornerRadius = v }), 0, 20),
            WeightRow("首选粗细", skin.FontName, skin.HighlightWeight, w => Update(s => s with { HighlightWeight = w })),
            padding,
            hint);
    }

    // 配色表：左列亮/暗标签，右侧各色横排均分；slot 对应颜色槽位
    private static UIElement ColorColumns(IReadOnlyList<(string Title, int Slot)> entries)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in entries) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < 3; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(grid, Legend("亮色"), 1, 0);
        Place(grid, Legend("暗色"), 2, 0);
        var skin = Skin;
        for (var i = 0; i < entries.Count; i++)
        {
            var (title, slot) = entries[i];
            var titleLabel = UiKit.Label(title, 11, FontWeights.Medium, secondary: true);
            titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            Place(grid, titleLabel, 0, i + 1);
            var light = new ColorSwatch(GetColor(skin.Light, slot).ToColor(), c => SetColor(false, slot, c.ToRgba()), $"{title}（亮色）");
            var dark = new ColorSwatch(GetColor(skin.Dark, slot).ToColor(), c => SetColor(true, slot, c.ToRgba()), $"{title}（暗色）");
            Place(grid, light, 1, i + 1);
            Place(grid, dark, 2, i + 1);
        }
        return grid;
    }

    private static TextBlock Legend(string text)
    {
        var l = UiKit.Label(text, 11, FontWeights.Medium, secondary: true);
        l.Margin = new Thickness(0, 0, 10, 0);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        return l;
    }

    private static void Place(Grid grid, FrameworkElement e, int row, int col)
    {
        e.Margin = new Thickness(col == 0 ? 0 : 6, row == 0 ? 0 : 8, col == 0 ? 10 : 6, 0);
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        grid.Children.Add(e);
    }

    private static RGBAColor GetColor(SkinColors c, int slot) => slot switch
    {
        0 => c.Background,
        1 => c.CandidateText,
        2 => c.IndexText,
        3 => c.Border,
        4 => c.Shadow,
        5 => c.HighlightBackground,
        6 => c.HighlightText,
        7 => c.HighlightIndexText,
        8 => c.CodeText,
        _ => c.HighlightCodeText
    };

    private static SkinColors WithColor(SkinColors c, int slot, RGBAColor v) => slot switch
    {
        0 => c with { Background = v },
        1 => c with { CandidateText = v },
        2 => c with { IndexText = v },
        3 => c with { Border = v },
        4 => c with { Shadow = v },
        5 => c with { HighlightBackground = v },
        6 => c with { HighlightText = v },
        7 => c with { HighlightIndexText = v },
        8 => c with { CodeText = v },
        _ => c with { HighlightCodeText = v }
    };

    private static void SetColor(bool dark, int slot, RGBAColor value) => Update(s => dark
        ? s with { Dark = WithColor(s.Dark, slot, value) }
        : s with { Light = WithColor(s.Light, slot, value) });

    private UIElement ShareForm(CandidateSkin skin)
    {
        var name = UiKit.Field(skin.Name, width: 180);
        name.LostKeyboardFocus += (_, _) => Update(s => s with { Name = name.Text });
        var author = UiKit.Field(skin.Author, width: 180);
        author.LostKeyboardFocus += (_, _) => Update(s => s with { Author = author.Text });
        _shareStatus.Text = "";
        var buttons = UiKit.HStack(8,
            UiKit.Button("导出到文件", ExportToFile),
            UiKit.Button("从文件导入", ImportFromFile),
            UiKit.Button("恢复默认", ResetSkin));
        return UiKit.VStack(10, UiKit.VStack(7, Row("皮肤名称", name), Row("作者", author)), UiKit.VStack(6, buttons, _shareStatus));
    }

    private void ExportToFile()
    {
        var skinName = Skin.Name;
        var dialog = new SaveFileDialog
        {
            Title = "导出皮肤配置",
            Filter = "皮肤配置 (*.json)|*.json",
            FileName = $"{(skinName.Length == 0 ? "meifeng-skin" : skinName)}.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, AppearancePrefs.ExportedSkinJson());
            _shareStatus.Text = $"已导出到 {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception e)
        {
            UiKit.Alert(Window.GetWindow(this), "皮肤配置", e.Message);
        }
    }

    private void ImportFromFile()
    {
        var dialog = new OpenFileDialog { Title = "导入皮肤配置", Filter = "皮肤配置 (*.json;*.txt)|*.json;*.txt|所有文件|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            AppearancePrefs.ImportSkinJson(File.ReadAllText(dialog.FileName));
            Build();
            _shareStatus.Text = $"已从 {Path.GetFileName(dialog.FileName)} 导入。";
        }
        catch (Exception e)
        {
            UiKit.Alert(Window.GetWindow(this), "皮肤配置", e.Message);
        }
    }

    private void ResetSkin()
    {
        if (!UiKit.Confirm(Window.GetWindow(this), "恢复默认皮肤？", "当前皮肤的名称、作者和所有外观参数都会被替换。", "恢复默认", destructive: true)) return;
        AppearancePrefs.RestoreDefaultSkin();
        Build();
        _shareStatus.Text = "已恢复默认皮肤。";
    }
}
