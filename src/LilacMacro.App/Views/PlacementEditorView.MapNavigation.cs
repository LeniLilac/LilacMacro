using System.Windows;
using System.Windows.Input;

namespace LilacMacro.App.Views;

public partial class PlacementEditorView
{
    private void MapViewportHost_OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        UnitPaletteOffset.X = Math.Clamp(UnitPaletteOffset.X, 0, Math.Max(0, eventArgs.NewSize.Width - UnitPalette.ActualWidth));
        UnitPaletteOffset.Y = Math.Clamp(UnitPaletteOffset.Y, 0, Math.Max(0, eventArgs.NewSize.Height - UnitPalette.ActualHeight));
    }

    private void MapViewport_OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (_mapFitMode) FitMapToViewport();
    }

    private void PlacementEditor_OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (!_wheelGesture.Observe(DateTimeOffset.UtcNow, MapViewport.IsMouseOver)) return;
        Point anchor = eventArgs.GetPosition(MapViewport);
        SetMapZoom(eventArgs.Delta > 0 ? _mapZoom * 1.15 : _mapZoom / 1.15, fitMode: false, anchor);
        eventArgs.Handled = true;
    }

    private void MapViewport_OnMouseMiddleButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Middle) return;
        _mapPanStart = eventArgs.GetPosition(MapViewport);
        _mapPanHorizontalOffset = MapViewport.HorizontalOffset;
        _mapPanVerticalOffset = MapViewport.VerticalOffset;
        MapViewport.Cursor = Cursors.ScrollAll;
        MapViewport.CaptureMouse();
        eventArgs.Handled = true;
    }

    private void MapViewport_OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_mapPanStart is not { } start || eventArgs.MiddleButton != MouseButtonState.Pressed) return;
        Point current = eventArgs.GetPosition(MapViewport);
        MapViewport.ScrollToHorizontalOffset(_mapPanHorizontalOffset - (current.X - start.X));
        MapViewport.ScrollToVerticalOffset(_mapPanVerticalOffset - (current.Y - start.Y));
        eventArgs.Handled = true;
    }

    private void MapViewport_OnMouseMiddleButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Middle) return;
        EndMapPan();
        eventArgs.Handled = true;
    }

    private void MapViewport_OnLostMouseCapture(object sender, MouseEventArgs eventArgs) => EndMapPan(releaseCapture: false);

    private void EndMapPan(bool releaseCapture = true)
    {
        _mapPanStart = null;
        MapViewport.Cursor = null;
        if (releaseCapture && Mouse.Captured == MapViewport) MapViewport.ReleaseMouseCapture();
    }

    private void FitMapToViewport()
    {
        if (_map is null || MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0) return;
        double fit = Math.Min(
            Math.Max(1, MapViewport.ActualWidth - 4) / _map.ImageWidth,
            Math.Max(1, MapViewport.ActualHeight - 4) / _map.ImageHeight);
        SetMapZoom(fit, fitMode: true);
    }

    private void QueueFitMapToViewport() => _ = Dispatcher.BeginInvoke(FitMapToViewport);

    private void SetMapZoom(double value, bool fitMode, Point? anchor = null)
    {
        double next = Math.Clamp(value, 0.1, 6);
        Point viewportAnchor = anchor ?? new Point(MapViewport.ViewportWidth / 2, MapViewport.ViewportHeight / 2);
        double logicalX = (MapViewport.HorizontalOffset + viewportAnchor.X - MapPanFrame.Padding.Left) / _mapZoom;
        double logicalY = (MapViewport.VerticalOffset + viewportAnchor.Y - MapPanFrame.Padding.Top) / _mapZoom;
        _mapFitMode = fitMode;
        _mapZoom = next;
        MapScale.ScaleX = next;
        MapScale.ScaleY = next;
        foreach (PlacementStepRowViewModel row in PlacementMarkers.Items.OfType<PlacementStepRowViewModel>()) row.SetZoom(next);
        _ = Dispatcher.BeginInvoke(() =>
        {
            double horizontal = fitMode && _map is not null
                ? MapPanFrame.Padding.Left + _map.ImageWidth * next / 2 - MapViewport.ViewportWidth / 2
                : MapPanFrame.Padding.Left + logicalX * next - viewportAnchor.X;
            double vertical = fitMode && _map is not null
                ? MapPanFrame.Padding.Top + _map.ImageHeight * next / 2 - MapViewport.ViewportHeight / 2
                : MapPanFrame.Padding.Top + logicalY * next - viewportAnchor.Y;
            MapViewport.ScrollToHorizontalOffset(horizontal);
            MapViewport.ScrollToVerticalOffset(vertical);
        });
    }
}
