using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace LilacMacro.App.Views;

internal sealed class ListReorderEventArgs<TItem>(
    TItem source,
    TItem target,
    bool insertAfter) : EventArgs
    where TItem : class
{
    public TItem Source { get; } = source;

    public TItem Target { get; } = target;

    public bool InsertAfter { get; } = insertAfter;
}

internal sealed class ListBoxReorderDragController<TItem>(ListBox list)
    where TItem : class
{
    private Point? _origin;
    private TItem? _dragged;
    private ListBoxItem? _adornedItem;
    private InsertionAdorner? _adorner;

    public event EventHandler<ListReorderEventArgs<TItem>>? ReorderRequested;

    public void Begin(TItem row, MouseButtonEventArgs eventArgs)
    {
        _dragged = row;
        _origin = eventArgs.GetPosition(list);
        list.SelectedItem = row;
        Mouse.Capture(list);
        eventArgs.Handled = true;
    }

    public void Continue(MouseEventArgs eventArgs)
    {
        if (_dragged is null || _origin is null || eventArgs.LeftButton != MouseButtonState.Pressed) return;
        Point current = eventArgs.GetPosition(list);
        if (Math.Abs(current.X - _origin.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _origin.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        TItem row = _dragged;
        try
        {
            Mouse.Capture(null);
            DragDrop.DoDragDrop(list, new DataObject(typeof(TItem), row), DragDropEffects.Move);
        }
        finally
        {
            Cancel();
        }
    }

    public void DragOver(DragEventArgs eventArgs)
    {
        if (!TryGetDragged(eventArgs.Data, out _) ||
            !TryFindTarget(eventArgs.GetPosition(list), out _, out ListBoxItem? container, out bool insertAfter))
        {
            eventArgs.Effects = DragDropEffects.None;
            ClearAdorner();
            eventArgs.Handled = true;
            return;
        }

        eventArgs.Effects = DragDropEffects.Move;
        ShowAdorner(container, insertAfter);
        eventArgs.Handled = true;
    }

    public void DragLeave()
    {
        if (!list.IsMouseOver) ClearAdorner();
    }

    public void Drop(DragEventArgs eventArgs)
    {
        try
        {
            if (!TryGetDragged(eventArgs.Data, out TItem? source) ||
                !TryFindTarget(
                    eventArgs.GetPosition(list),
                    out TItem? target,
                    out _,
                    out bool insertAfter))
            {
                eventArgs.Effects = DragDropEffects.None;
                return;
            }

            ReorderRequested?.Invoke(this, new ListReorderEventArgs<TItem>(source, target, insertAfter));
            eventArgs.Effects = DragDropEffects.Move;
        }
        finally
        {
            ClearAdorner();
            eventArgs.Handled = true;
        }
    }

    public void Cancel()
    {
        _dragged = null;
        _origin = null;
        ClearAdorner();
        if (ReferenceEquals(Mouse.Captured, list)) Mouse.Capture(null);
    }

    private bool TryFindTarget(
        Point position,
        [NotNullWhen(true)] out TItem? target,
        [NotNullWhen(true)] out ListBoxItem? container,
        out bool insertAfter)
    {
        DependencyObject? hit = list.InputHitTest(position) as DependencyObject;
        container = hit is null ? null : ItemsControl.ContainerFromElement(list, hit) as ListBoxItem;
        target = container?.DataContext as TItem;
        insertAfter = container is not null &&
            position.Y - container.TranslatePoint(new Point(), list).Y > container.ActualHeight / 2;
        return target is not null && container is not null;
    }

    private static bool TryGetDragged(
        IDataObject data,
        [NotNullWhen(true)] out TItem? row)
    {
        row = data.GetData(typeof(TItem)) as TItem;
        return row is not null;
    }

    private void ShowAdorner(ListBoxItem item, bool insertAfter)
    {
        if (ReferenceEquals(item, _adornedItem) && _adorner?.InsertAfter == insertAfter) return;
        ClearAdorner();
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(item);
        if (layer is null) return;
        Brush brush = list.TryFindResource("PinkBrush") as Brush ?? Brushes.DeepPink;
        _adornedItem = item;
        _adorner = new InsertionAdorner(item, insertAfter, brush);
        layer.Add(_adorner);
    }

    private void ClearAdorner()
    {
        if (_adornedItem is not null && _adorner is not null)
        {
            AdornerLayer.GetAdornerLayer(_adornedItem)?.Remove(_adorner);
        }
        _adornedItem = null;
        _adorner = null;
    }

    private sealed class InsertionAdorner(UIElement element, bool insertAfter, Brush brush) : Adorner(element)
    {
        private readonly Pen _pen = new(brush, 3);

        public bool InsertAfter { get; } = insertAfter;

        protected override void OnRender(DrawingContext drawingContext)
        {
            double y = InsertAfter ? AdornedElement.RenderSize.Height - 1.5 : 1.5;
            drawingContext.DrawLine(_pen, new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
        }
    }
}
