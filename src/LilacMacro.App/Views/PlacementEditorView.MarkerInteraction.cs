using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LilacMacro.App.Views;

public partial class PlacementEditorView
{
    private PlacementCursorMode _cursorMode = PlacementCursorMode.Place;

    private void CursorMode_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not RadioButton { IsChecked: true, Tag: string value } ||
            !Enum.TryParse(value, ignoreCase: true, out PlacementCursorMode mode))
        {
            return;
        }

        _cursorMode = mode;
        ApplyCursorModeCursor();
        RefreshPlacementMarkers();
    }

    private void ApplyCursorModeCursor() =>
        MapSurface.Cursor = _cursorMode == PlacementCursorMode.Place ? Cursors.Cross : Cursors.Arrow;

    private void RefreshPlacementMarkers()
    {
        if (_session.Document is null) return;
        PlacementMarkers.ItemsSource = PlacementStepRowFactory.Create(
                _session.CurrentRoute,
                _map?.ImageWidth ?? _session.Document.ImageWidth,
                _map?.ImageHeight ?? _session.Document.ImageHeight,
                _cursorMode)
            .Where(row => row.IsPlacement)
            .ToArray();
    }

    private void MapSurface_OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_cursorMode != PlacementCursorMode.Place)
        {
            ClearNearbyPlacementMarkers();
            return;
        }

        Point pointer = eventArgs.GetPosition(MapSurface);
        foreach (PlacementStepRowViewModel row in PlacementMarkers.Items.OfType<PlacementStepRowViewModel>())
        {
            row.SetNearPointer(PlacementMarkerPresentation.IsNearPointer(
                row.Step.X,
                row.Step.Y,
                pointer.X,
                pointer.Y,
                _mapZoom));
        }
    }

    private void MapSurface_OnMouseLeave(object sender, MouseEventArgs eventArgs) =>
        ClearNearbyPlacementMarkers();

    private void ClearNearbyPlacementMarkers()
    {
        foreach (PlacementStepRowViewModel row in PlacementMarkers.Items.OfType<PlacementStepRowViewModel>())
        {
            row.SetNearPointer(false);
        }
    }

    private async void MapSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_cursorMode != PlacementCursorMode.Place) return;
        eventArgs.Handled = true;
        await AddPlacementAtAsync(eventArgs.GetPosition(MapSurface));
    }

    private async void PlacementMarker_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: PlacementStepRowViewModel row }) return;

        if (eventArgs.OriginalSource is DependencyObject source &&
            HasButtonAncestor(source, sender as DependencyObject))
        {
            return;
        }

        if (_cursorMode == PlacementCursorMode.Select)
        {
            _timelinePanel.SelectStep(row.Step.Id);
            return;
        }

        eventArgs.Handled = true;
        await AddPlacementAtAsync(eventArgs.GetPosition(MapSurface));
    }

    private async void PlacementMarkerDelete_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_cursorMode != PlacementCursorMode.Select ||
            sender is not Button { Tag: PlacementStepRowViewModel row })
        {
            return;
        }

        eventArgs.Handled = true;
        await RunEditAsync(() => _session.DeleteStepAsync(row.Index));
    }

    private static bool HasButtonAncestor(DependencyObject source, DependencyObject? markerRoot)
    {
        for (DependencyObject? current = source;
             current is not null && !ReferenceEquals(current, markerRoot);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase) return true;
        }

        return false;
    }

    private async Task AddPlacementAtAsync(Point point)
    {
        if (_cursorMode != PlacementCursorMode.Place || _map is null || !_session.CanEdit) return;
        Focus();
        int x = (int)Math.Round(Math.Clamp(point.X, 0, _map.ImageWidth - 1));
        int y = (int)Math.Round(Math.Clamp(point.Y, 0, _map.ImageHeight - 1));
        await RunEditAsync(() => _session.AddPlacementAsync(x, y));
    }
}
