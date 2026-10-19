using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MeifengWubi.Ui;

// ListView 拖动排序：按下移动超过阈值开始拖动，显示插入线，松手回调 (原行, 目标行)。目标行指移动后所在的位置
internal sealed class ReorderHelper
{
    private readonly ListView _list;
    private readonly Func<int, bool> _canDrag;
    private readonly Action<int> _blocked;
    private readonly Action<int, int> _moved;
    private Point _start;
    private int _source = -1;
    private InsertionAdorner? _adorner;

    private ReorderHelper(ListView list, Func<int, bool> canDrag, Action<int> blocked, Action<int, int> moved)
    {
        _list = list;
        _canDrag = canDrag;
        _blocked = blocked;
        _moved = moved;
        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(list);
            _source = RowAt(e.GetPosition(list));
        };
        list.PreviewMouseMove += OnMouseMove;
        list.DragOver += OnDragOver;
        list.DragLeave += (_, _) => RemoveAdorner();
        list.Drop += OnDrop;
    }

    // blocked：试图拖动不可移动的行时回调
    public static ReorderHelper Attach(ListView list, Func<int, bool> canDrag, Action<int> blocked, Action<int, int> moved) =>
        new(list, canDrag, blocked, moved);

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _source < 0) return;
        var p = e.GetPosition(_list);
        if (Math.Abs(p.X - _start.X) < 4 && Math.Abs(p.Y - _start.Y) < 4) return;
        var source = _source;
        _source = -1;
        if (!_canDrag(source))
        {
            _blocked(source);
            return;
        }
        if (_list.ItemContainerGenerator.ContainerFromIndex(source) is ListViewItem item) item.Opacity = 0.45;
        DragDrop.DoDragDrop(_list, new DataObject("meifeng-row", source), DragDropEffects.Move);
        if (_list.ItemContainerGenerator.ContainerFromIndex(source) is ListViewItem back) back.Opacity = 1;
        RemoveAdorner();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("meifeng-row"))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        var (gap, y) = GapAt(e.GetPosition(_list));
        if (gap < 0) return;
        _adorner ??= CreateAdorner();
        _adorner?.Update(y);
        AutoScroll(e.GetPosition(_list));
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        RemoveAdorner();
        if (e.Data.GetData("meifeng-row") is not int source) return;
        var (gap, _) = GapAt(e.GetPosition(_list));
        if (gap < 0) return;
        var target = gap > source ? gap - 1 : gap;
        if (target != source) _moved(source, target);
    }

    // 鼠标所在的插入缝：行的上半部分插在它前面，下半部分插在它后面
    private (int Gap, double Y) GapAt(Point p)
    {
        var count = _list.Items.Count;
        if (count == 0) return (-1, 0);
        for (var i = 0; i < count; i++)
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(i) is not ListViewItem item) continue;
            var top = item.TranslatePoint(new Point(0, 0), _list).Y;
            var bottom = top + item.ActualHeight;
            if (p.Y < top + item.ActualHeight / 2) return (i, top);
            if (p.Y < bottom) return (i + 1, bottom);
        }
        var last = _list.ItemContainerGenerator.ContainerFromIndex(count - 1) as ListViewItem;
        var y = last == null ? 0 : last.TranslatePoint(new Point(0, last.ActualHeight), _list).Y;
        return (count, y);
    }

    private int RowAt(Point p)
    {
        var hit = _list.InputHitTest(p) as DependencyObject;
        while (hit != null && hit is not ListViewItem) hit = VisualTreeHelper.GetParent(hit);
        return hit is ListViewItem item ? _list.ItemContainerGenerator.IndexFromContainer(item) : -1;
    }

    private void AutoScroll(Point p)
    {
        if (FindScroll(_list) is not { } scroll) return;
        if (p.Y < 16) scroll.LineUp();
        else if (p.Y > _list.ActualHeight - 16) scroll.LineDown();
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScroll(child) is { } found) return found;
        }
        return null;
    }

    private InsertionAdorner? CreateAdorner()
    {
        var layer = AdornerLayer.GetAdornerLayer(_list);
        if (layer == null) return null;
        var adorner = new InsertionAdorner(_list);
        layer.Add(adorner);
        return adorner;
    }

    private void RemoveAdorner()
    {
        if (_adorner == null) return;
        AdornerLayer.GetAdornerLayer(_list)?.Remove(_adorner);
        _adorner = null;
    }

    // 插入位置的强调色横线
    private sealed class InsertionAdorner(UIElement owner) : Adorner(owner)
    {
        private double _y;

        public void Update(double y)
        {
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var brush = (AdornedElement as FrameworkElement)?.TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
            var pen = new Pen(brush, 2);
            var w = AdornedElement.RenderSize.Width;
            dc.DrawLine(pen, new Point(4, _y), new Point(w - 4, _y));
            dc.DrawEllipse(brush, null, new Point(4, _y), 3, 3);
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
    }
}
