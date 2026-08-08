using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LilacMacro.App.Views;

public partial class PlacementEditorView
{
    private PlacementMarkerDragState? _placementMarkerDrag;

    private void PlacementMarker_OnDragStarted(object sender, DragStartedEventArgs eventArgs)
    {
        if (_map is null || !_session.CanEdit ||
            sender is not Thumb { DataContext: PlacementStepRowViewModel row } ||
            PlacementMarkers.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement container)
        {
            return;
        }

        _placementMarkerDrag = new PlacementMarkerDragState(
            row,
            container,
            row.Step.X,
            row.Step.Y);
        MapSurface.Cursor = Cursors.Arrow;
        eventArgs.Handled = true;
    }

    private void PlacementMarker_OnDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        if (_map is null || _placementMarkerDrag is not { } drag) return;

        Point point = Mouse.GetPosition(MapSurface);
        int x = (int)Math.Round(Math.Clamp(point.X, 0, _map.ImageWidth - 1));
        int y = (int)Math.Round(Math.Clamp(point.Y, 0, _map.ImageHeight - 1));
        drag.CurrentX = x;
        drag.CurrentY = y;
        drag.Container.RenderTransform = new TranslateTransform(
            x - drag.OriginalX,
            y - drag.OriginalY);
        eventArgs.Handled = true;
    }

    private async void PlacementMarker_OnDragCompleted(object sender, DragCompletedEventArgs eventArgs)
    {
        if (_placementMarkerDrag is not { } drag) return;

        _placementMarkerDrag = null;
        drag.Container.RenderTransform = Transform.Identity;
        MapSurface.Cursor = Cursors.Cross;
        eventArgs.Handled = true;

        if (eventArgs.Canceled ||
            (drag.CurrentX == drag.OriginalX && drag.CurrentY == drag.OriginalY))
        {
            RefreshWorkspace();
            return;
        }

        await RunEditAsync(() => _session.MovePlacementAsync(
            drag.Row.Step.Id,
            drag.CurrentX,
            drag.CurrentY));
    }

    private sealed class PlacementMarkerDragState(
        PlacementStepRowViewModel row,
        FrameworkElement container,
        int originalX,
        int originalY)
    {
        public PlacementStepRowViewModel Row { get; } = row;

        public FrameworkElement Container { get; } = container;

        public int OriginalX { get; } = originalX;

        public int OriginalY { get; } = originalY;

        public int CurrentX { get; set; } = originalX;

        public int CurrentY { get; set; } = originalY;
    }
}
