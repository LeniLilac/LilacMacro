using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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

internal static class ListReorderDestination
{
    public static int Resolve(int sourceIndex, int targetIndex, bool insertAfter, int itemCount)
    {
        if (sourceIndex < 0 || sourceIndex >= itemCount) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        if (targetIndex < 0 || targetIndex >= itemCount) throw new ArgumentOutOfRangeException(nameof(targetIndex));

        int insertionBoundary = targetIndex + (insertAfter ? 1 : 0);
        int destination = sourceIndex < insertionBoundary
            ? insertionBoundary - 1
            : insertionBoundary;
        return Math.Clamp(destination, 0, itemCount - 1);
    }
}

internal readonly record struct ListReorderHit(int TargetIndex, bool InsertAfter);

internal static class ListReorderHitTest
{
    public static ListReorderHit Resolve(
        double pointerY,
        IReadOnlyList<double> itemCenters,
        int sourceIndex)
    {
        if (sourceIndex < -1 || sourceIndex >= itemCenters.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        if (itemCenters.Count == 0 || itemCenters.Count == 1 && sourceIndex == 0)
            throw new ArgumentException("Reordering requires at least one available target.", nameof(itemCenters));

        int lastTarget = -1;
        for (int index = 0; index < itemCenters.Count; index++)
        {
            if (index == sourceIndex) continue;
            lastTarget = index;
            if (pointerY <= itemCenters[index]) return new ListReorderHit(index, InsertAfter: false);
        }

        return new ListReorderHit(lastTarget, InsertAfter: true);
    }
}

internal sealed class ListBoxReorderDragController<TItem>
    where TItem : class
{
    private readonly ListBox _list;
    private readonly FrameworkElement _previewHost;
    private readonly DispatcherTimer _edgeScrollTimer;
    private Point? _origin;
    private TItem? _dragged;
    private bool _isDragging;
    private ListBoxItem? _draggedContainer;
    private double _draggedOpacity;
    private ListBoxItem? _adornedItem;
    private AdornerLayer? _insertionAdornerLayer;
    private InsertionAdorner? _insertionAdorner;
    private AdornerLayer? _previewAdornerLayer;
    private DragPreviewAdorner? _previewAdorner;
    private TItem? _lastTarget;
    private bool _lastInsertAfter;

    public ListBoxReorderDragController(ListBox list)
    {
        _list = list;
        _previewHost = list;
        _edgeScrollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(45),
        };
        _edgeScrollTimer.Tick += EdgeScrollTimer_OnTick;
    }

    public event EventHandler<ListReorderEventArgs<TItem>>? ReorderRequested;

    public event EventHandler? DragEnded;

    public void Begin(TItem row, MouseButtonEventArgs eventArgs)
    {
        _dragged = row;
        _origin = eventArgs.GetPosition(_list);
        _list.SelectedItem = row;
        Mouse.Capture(_list, CaptureMode.Element);
        eventArgs.Handled = true;
    }

    public void Continue(MouseEventArgs eventArgs)
    {
        if (_dragged is null || _origin is null || eventArgs.LeftButton != MouseButtonState.Pressed) return;
        Point current = eventArgs.GetPosition(_list);
        if (!_isDragging &&
            Math.Abs(current.X - _origin.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _origin.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (!_isDragging)
        {
            BeginVisualDrag(_dragged, _origin.Value);
            _isDragging = true;
            _edgeScrollTimer.Start();
        }

        UpdateDrag(current);
        eventArgs.Handled = true;
    }

    public void Complete(MouseButtonEventArgs eventArgs)
    {
        try
        {
            if (!_isDragging || _dragged is null)
            {
                return;
            }

            UpdateDropTarget(eventArgs.GetPosition(_list));
            if (_lastTarget is not null)
                ReorderRequested?.Invoke(
                    this,
                    new ListReorderEventArgs<TItem>(_dragged, _lastTarget, _lastInsertAfter));
            _list.SelectedItem = _dragged;
            eventArgs.Handled = true;
        }
        finally
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        bool hadPendingDrag = _dragged is not null || _origin is not null || _isDragging ||
                              _previewAdorner is not null || _insertionAdorner is not null;
        RestoreDraggedContainer();
        _edgeScrollTimer.Stop();
        _dragged = null;
        _origin = null;
        _isDragging = false;
        _lastTarget = null;
        _lastInsertAfter = false;
        ClearInsertionAdorner();
        ClearPreviewAdorner();
        if (ReferenceEquals(Mouse.Captured, _list)) Mouse.Capture(null);
        if (hadPendingDrag) DragEnded?.Invoke(this, EventArgs.Empty);
    }

    private void BeginVisualDrag(TItem row, Point origin)
    {
        _draggedContainer = _list.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;
        if (_draggedContainer is null) return;

        _draggedOpacity = _draggedContainer.Opacity;
        ImageBrush snapshot = PlacementTimelineSnapshot.Capture(_draggedContainer);
        Point topLeft = _draggedContainer.TranslatePoint(new Point(), _previewHost);
        Point hostOrigin = _list.TranslatePoint(origin, _previewHost);
        _draggedContainer.Opacity = 0;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(_previewHost);
        if (layer is null) return;
        _previewAdorner = new DragPreviewAdorner(
            _previewHost,
            snapshot,
            Math.Max(1, _draggedContainer.ActualWidth - 12),
            _draggedContainer.ActualHeight,
            topLeft.X + 6,
            hostOrigin.Y - topLeft.Y);
        layer.Add(_previewAdorner);
        _previewAdornerLayer = layer;
        _previewAdorner.Update(hostOrigin);
    }

    private void RestoreDraggedContainer()
    {
        if (_draggedContainer is not null) _draggedContainer.Opacity = _draggedOpacity;
        _draggedContainer = null;
    }

    private void UpdateDrag(Point position)
    {
        _previewAdorner?.Update(_list.TranslatePoint(position, _previewHost));
        UpdateDropTarget(position);
    }

    private void UpdateDropTarget(Point position)
    {
        if (!TryFindTarget(position, out TItem? target, out ListBoxItem? container, out bool insertAfter))
        {
            ClearInsertionAdorner();
            return;
        }

        _lastTarget = target;
        _lastInsertAfter = insertAfter;
        ShowInsertionAdorner(container, insertAfter);
    }

    private bool TryFindTarget(
        Point position,
        [NotNullWhen(true)] out TItem? target,
        [NotNullWhen(true)] out ListBoxItem? container,
        out bool insertAfter)
    {
        target = null;
        container = null;
        insertAfter = false;
        if (_list.Items.Count == 0) return false;

        int sourceIndex = _dragged is null ? -1 : _list.Items.IndexOf(_dragged);
        if (_dragged is not null && sourceIndex < 0) return false;
        if (_list.Items.Count == 1 && sourceIndex == 0) return false;

        double[] centers = new double[_list.Items.Count];
        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem candidate)
                return false;
            double top = candidate.TranslatePoint(new Point(), _list).Y;
            centers[index] = top + candidate.ActualHeight / 2;
        }

        ListReorderHit hit = ListReorderHitTest.Resolve(position.Y, centers, sourceIndex);
        container = _list.ItemContainerGenerator.ContainerFromIndex(hit.TargetIndex) as ListBoxItem;
        target = container?.DataContext as TItem;
        insertAfter = hit.InsertAfter;
        return target is not null && container is not null;
    }

    private void EdgeScrollTimer_OnTick(object? sender, EventArgs eventArgs)
    {
        if (!_isDragging || !ReferenceEquals(Mouse.Captured, _list))
        {
            _edgeScrollTimer.Stop();
            return;
        }

        Point position = Mouse.GetPosition(_list);
        ScrollNearEdge(position);
        UpdateDrag(position);
    }

    private void ScrollNearEdge(Point position)
    {
        const double edge = 34;
        ScrollViewer? inner = FindVisualChild<ScrollViewer>(_list);
        ScrollViewer? viewer = inner is { ScrollableHeight: > 0.5 }
            ? inner
            : FindAncestorScrollViewer(_list);
        if (viewer is null) return;
        Point viewportPosition = _list.TranslatePoint(position, viewer);
        if (viewportPosition.Y < edge)
        {
            viewer.ScrollToVerticalOffset(Math.Max(0, viewer.VerticalOffset - 26));
        }
        else if (viewportPosition.Y > viewer.ViewportHeight - edge)
        {
            viewer.ScrollToVerticalOffset(Math.Min(viewer.ScrollableHeight, viewer.VerticalOffset + 26));
        }
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject origin)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(origin);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer viewer) return viewer;
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private void ShowInsertionAdorner(ListBoxItem item, bool insertAfter)
    {
        if (ReferenceEquals(item, _adornedItem) && _insertionAdorner?.InsertAfter == insertAfter) return;
        ClearInsertionAdorner();
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(item);
        if (layer is null) return;
        Brush brush = _list.TryFindResource("PinkBrush") as Brush ?? Brushes.DeepPink;
        _adornedItem = item;
        _insertionAdorner = new InsertionAdorner(item, insertAfter, brush);
        layer.Add(_insertionAdorner);
        _insertionAdornerLayer = layer;
    }

    private void ClearInsertionAdorner()
    {
        if (_insertionAdorner is not null) _insertionAdornerLayer?.Remove(_insertionAdorner);
        _adornedItem = null;
        _insertionAdornerLayer = null;
        _insertionAdorner = null;
    }

    private void ClearPreviewAdorner()
    {
        if (_previewAdorner is not null) _previewAdornerLayer?.Remove(_previewAdorner);
        _previewAdornerLayer = null;
        _previewAdorner = null;
    }

    private sealed class InsertionAdorner : Adorner
    {
        private readonly Pen _pen;

        public InsertionAdorner(UIElement element, bool insertAfter, Brush brush) : base(element)
        {
            InsertAfter = insertAfter;
            IsHitTestVisible = false;
            _pen = new Pen(brush, 3);
            _pen.Freeze();
        }

        public bool InsertAfter { get; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            double y = InsertAfter ? AdornedElement.RenderSize.Height - 1.5 : 1.5;
            drawingContext.DrawLine(_pen, new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
        }
    }

    private sealed class DragPreviewAdorner : Adorner
    {
        private readonly Brush _snapshot;
        private readonly double _width;
        private readonly double _height;
        private readonly double _left;
        private readonly double _grabOffsetY;
        private Point _position;

        public DragPreviewAdorner(
            UIElement element,
            Brush snapshot,
            double width,
            double height,
            double left,
            double grabOffsetY) : base(element)
        {
            _snapshot = snapshot;
            _width = width;
            _height = height;
            _left = left;
            _grabOffsetY = grabOffsetY;
            IsHitTestVisible = false;
        }

        public void Update(Point position)
        {
            _position = position;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Rect shadow = new(_left + 3, _position.Y - _grabOffsetY + 4, _width, _height);
            Rect row = new(_left, _position.Y - _grabOffsetY, _width, _height);
            drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, shadow, 4, 4);
            drawingContext.PushOpacity(0.9);
            drawingContext.DrawRoundedRectangle(_snapshot, null, row, 4, 4);
            drawingContext.Pop();
        }
    }
}
