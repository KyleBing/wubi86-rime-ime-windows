using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meifeng.Core.Lexicon;

namespace MeifengWubi.Ui;

// 同编码词条表：词条、编码、权重、词库、分组五列，权重高在前，最多列 40 条
internal sealed class LexiconTable : Border
{
    private readonly ListView _list = new() { BorderThickness = new Thickness(0), FontSize = 12 };
    private readonly TextBlock _message = UiKit.Label("", 12, secondary: true, wrap: true);
    private readonly TextBlock _footer = UiKit.Label("", 11, secondary: true);

    public LexiconTable(double height)
    {
        Height = height;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        var view = new GridView();
        string[] titles = DictLexiconHit.ColumnTitles;
        double[] widths = [110, 64, 56, 96, 90];
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i;
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"[{index}]"));
            if (index is 1 or 2) factory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
            view.Columns.Add(new GridViewColumn { Header = titles[i], Width = widths[i], CellTemplate = new DataTemplate { VisualTree = factory } });
        }
        _list.View = view;
        _message.Margin = new Thickness(8, 6, 8, 6);
        _footer.Margin = new Thickness(8, 2, 8, 4);
        var dock = new DockPanel();
        DockPanel.SetDock(_footer, Dock.Bottom);
        dock.Children.Add(_footer);
        dock.Children.Add(new Grid { Children = { _list, _message } });
        Child = dock;
        ShowMessage("");
    }

    // 只显示一行说明（查找中、没有同编码词等）
    public void ShowMessage(string text)
    {
        _list.ItemsSource = null;
        _list.Visibility = Visibility.Collapsed;
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
        _footer.Visibility = Visibility.Collapsed;
    }

    public void ShowHits(IEnumerable<DictLexiconHit> hits)
    {
        var (shown, remaining) = DictLexiconHit.SortedForTable(hits);
        _list.ItemsSource = shown.Select(h => h.TableColumns).ToList();
        _list.Visibility = Visibility.Visible;
        _message.Visibility = Visibility.Collapsed;
        _footer.Text = remaining > 0 ? $"还有 {remaining} 条未列出" : "";
        _footer.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
