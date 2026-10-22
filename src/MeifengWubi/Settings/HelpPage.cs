using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 帮助页：预置编码、特殊按键、联系方式与开源声明
internal static class HelpPage
{
    public static FrameworkElement Build()
    {
        var presets = HelpList([
            ("date", "当前日期：2026-09-23、2026年9月23日、星期三，翻页可见日期加时间"),
            ("time", "当前时间：11:22:05、11:22、秒级时间戳、11时22分，翻页可见毫秒时间戳"),
            ("week", "星期与周数：星期三、周三、第39周、2026年第39周、Wednesday 等"),
            ("选词与翻页", "与普通候选相同：数字键、空格、; ' 选词，翻页键按「输入习惯」页设置")
        ]);
        var lookup = HelpList([
            ("z + 拼音", "拼音反查：不会五笔拆字时，以 z 开头输入拼音，候选中选字上屏。单字后面会标出词库里的五笔编码，词组不现造编码"),
            ("'", "反查结束符，拼音输完可按 ' 结束反查")
        ]);
        var keys = HelpList([
            ("造词快捷键", "默认 Ctrl+Alt+N，可在「输入习惯」页修改。弹出窗口添加自造词，写入用户词当前分组；两分钟内刚上屏的词会自动填入，并列出同编码词来自哪个词库"),
            ("删除候选", "候选出现时，按与造词相同的修饰键加数字（默认 Ctrl+Alt+1–9）。确认框会显示词库、权重、备注和同编码词，确认后从该词库删除"),
            ("候选排序", "候选出现时按排序快捷键（默认 Ctrl+Alt+0，可在「输入习惯」页单独修改）。这段时间输入法接管这组键，窗口会弹到最前面。拖动调整顺序，只改被拖动词条的权重，幅度可选 5 或 10。关闭窗口后重新加载词库"),
            ("单按 Shift", "中 / 英文模式切换；英文模式下按键直接上屏"),
            ("光标提示", "输入框出现光标时，在旁边显示当前是中文还是英文。可在「输入习惯」页关闭"),
            ("空格", "上屏第一个候选"),
            ("1 – 9", "选择对应序号的候选（仅数量圆点模式下同样有效）"),
            ("; '", "选第二、第三候选（需在「输入习惯」页开启）"),
            ("- = / , .", "候选翻页，方式在「输入习惯」页设置"),
            ("Esc", "清空当前编码"),
            ("回车", "默认直接上屏已输入的编码字母；开启回车清码后改为清空编码"),
            ("四码唯一", "输入满四码且只有一个候选时自动上屏"),
            ("空码清空", "打满四码仍无候选时自动清空编码。可在「输入习惯」页开启"),
            ("回车清码", "编码中按回车只清空编码，不上屏字母。可在「输入习惯」页开启")
        ]);
        var switches = HelpList([
            ("Ctrl+Shift+1", "切换输入方案（五笔 / 拼音）"),
            ("Ctrl+Shift+2", "中 / 英文切换"),
            ("Ctrl+Shift+3", "全角 / 半角切换"),
            ("Ctrl+Shift+5", "常用 / 扩展字符集切换"),
            ("Ctrl+A / E / B / F", "编码中移动光标：行首、行尾、左移、右移"),
            ("Ctrl+H / Ctrl+G", "编码中删除一个字母 / 取消输入")
        ]);
        var appearance = HelpList([
            ("仅圆点", "不展开候选词，只用小圆点表示当前页候选数量，适合熟练盲打"),
            ("不显示", "完全隐藏候选框，只保留编码下划线，数字键、空格等选词照常"),
            ("距光标上 / 左", "在「外观配置 › 边距与位置」中调整候选窗与光标的距离"),
            ("皮肤分享", "外观可导出为文件，在其他设备（含 macOS 版）从文件导入")
        ]);
        var contact = UiKit.VStack(8, ContactRow("邮箱", "kylebing@163.com"), ContactRow("QQ 群", "878750538"));
        var openSource = HelpList([
            ("librime", "BSD 3-Clause License。中州韵输入法引擎，Copyright © RIME Developers。https://github.com/rime/librime"),
            ("Rime prelude", "BSD 3-Clause License。共享预设 default.yaml、key_bindings.yaml、punctuation.yaml、symbols.yaml。https://github.com/rime/rime-prelude"),
            ("OpenCC", "Apache License 2.0。简繁转换用到的配置和词典。https://github.com/BYVoid/OpenCC"),
            ("极点码表", "内置五笔方案与词库 rime-wubi86-jidian。https://github.com/KyleBing/rime-wubi86-jidian")
        ]);
        var stack = UiKit.VStack(12,
            UiKit.Card("预置编码", "输入以下编码，候选中会出现基于系统时间的内容。", presets),
            UiKit.Card("拼音反查", null, lookup),
            UiKit.Card("常用按键", null, keys),
            UiKit.Card("快捷开关", null, switches),
            UiKit.Card("特殊显示", null, appearance),
            UiKit.Card("反馈与联系", "使用问题、建议或词库错误，欢迎联系作者。", contact),
            UiKit.Card("开源声明", "输入法使用了下列开源组件，版权归原作者所有。", openSource));
        return UiKit.Page("帮助", "预置输入功能与特殊按键说明。", stack);
    }

    private static TextBlock KeyLabel(string text)
    {
        var key = UiKit.Label(text, 12, FontWeights.Medium);
        key.FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");
        key.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        key.Width = 140;
        key.VerticalAlignment = VerticalAlignment.Top;
        return key;
    }

    // 两列说明列表：左列按键 / 编码（等宽字体），右列说明文字（可选中复制）
    private static UIElement HelpList(IReadOnlyList<(string Key, string Text)> items)
    {
        var list = new StackPanel();
        foreach (var (key, text) in items)
        {
            var grid = new Grid { Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 8, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(152) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var body = UiKit.SelectableText(text, 12);
            body.MaxWidth = 460;
            body.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(body, 1);
            grid.Children.Add(KeyLabel(key));
            grid.Children.Add(body);
            list.Children.Add(grid);
        }
        return list;
    }

    // 邮箱 / QQ 群一键复制
    private static UIElement ContactRow(string label, string value)
    {
        var key = KeyLabel(label);
        var text = UiKit.SelectableText(value, 12);
        text.Width = double.NaN;
        var copy = UiKit.Button("复制", () =>
        {
            try { Clipboard.SetText(value); } catch { }
        });
        return UiKit.HStack(12, key, text, copy);
    }
}
