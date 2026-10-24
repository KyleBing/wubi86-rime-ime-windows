using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Meifeng.Core.Config;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 标点页（对应 macOS PunctuationSettingsView）：可改输出内容，并选择直接上屏或弹出候选列表
internal static class PunctuationPage
{
    public static FrameworkElement Build()
    {
        var body = new StackPanel();
        Fill(body);
        return UiKit.Page("标点",
            "标点由输入法处理。默认每键直接上屏第一项。引号和单引号第一次出前引号，第二次出后引号。输出用空格分开多项，可直接修改。改成候选列表后会弹出全部符号供选择。",
            UiKit.Card("标点输出", null, body));
    }

    private static void Fill(StackPanel stack)
    {
        stack.Children.Clear();
        var restore = UiKit.Button("恢复默认", () =>
        {
            if (!UiKit.Confirm(Window.GetWindow(stack), "恢复默认标点？", "输出内容和输出方式都会回到默认，每键直接上屏第一项。", "恢复默认")) return;
            PunctuationPrefs.RestoreDefaults();
            Fill(stack);
        });
        restore.HorizontalAlignment = HorizontalAlignment.Left;
        var rows = new List<UIElement>
        {
            restore,
            UiKit.HStack(12, Header("按键", 36), Header("输出", 280), Header("方式", 196))
        };
        var group = 0;
        foreach (var item in PunctuationPrefs.Items)
        {
            var key = UiKit.Label(item.Key, 13, FontWeights.Medium);
            key.Width = 36;
            key.FontFamily = new FontFamily("Cascadia Mono, Consolas");

            var symbols = UiKit.Field(string.Join(" ", PunctuationPrefs.SymbolsFor(item.Key)), width: 280);
            // 空内容会回到默认符号，把输入框同步成实际保存的结果
            void Commit()
            {
                PunctuationPrefs.SetSymbols(PunctuationPrefs.ParseSymbols(symbols.Text), item.Key);
                UiKit.Set(symbols, string.Join(" ", PunctuationPrefs.SymbolsFor(item.Key)));
            }
            symbols.LostKeyboardFocus += (_, _) => Commit();
            symbols.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
            };

            // 每行单独成组，点一下另一项就切换
            var name = $"punct{group++}";
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            var current = PunctuationPrefs.ModeFor(item.Key);
            foreach (var mode in Enum.GetValues<PunctuationMode>())
            {
                var radio = new RadioButton { Content = mode.Title(), GroupName = name, IsChecked = mode == current, FontSize = 12, Margin = new Thickness(0, 0, 10, 0), MinWidth = 0 };
                radio.Checked += (_, _) => PunctuationPrefs.SetMode(mode, item.Key);
                modes.Children.Add(radio);
            }
            var row = UiKit.HStack(12, key, symbols, modes);
            rows.Add(row);
        }
        UiKit.AddSpaced(stack, 8, rows);
    }

    private static TextBlock Header(string text, double width)
    {
        var l = UiKit.Label(text, 11, secondary: true);
        l.Width = width;
        return l;
    }
}
