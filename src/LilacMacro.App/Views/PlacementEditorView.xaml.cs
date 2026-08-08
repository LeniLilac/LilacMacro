using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LilacMacro.App.Notifications;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlacementEditorView : UserControl
{
    private readonly PlacementEditorSession _session;
    private readonly PlacementTimelinePanel _timelinePanel;
    private PlacementTimelineWindow? _timelineWindow;
    private PlacementMapCardViewModel? _map;
    private bool _refreshing;
    private double _mapZoom = 1;
    private bool _mapFitMode = true;
    private Point? _mapPanStart;
    private double _mapPanHorizontalOffset;
    private double _mapPanVerticalOffset;

    public PlacementEditorView()
    {
        InitializeComponent();
        string placementRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "placements");
        _session = new PlacementEditorSession(new PlacementSetupStore(placementRoot));
        _timelinePanel = new PlacementTimelinePanel();
        _timelinePanel.SetupChanged += TimelinePanel_OnSetupChanged;
        _timelinePanel.PopOutRequested += TimelinePanel_OnPopOutRequested;
        TimelineHost.Content = _timelinePanel;
    }

    public event EventHandler? BackRequested;

    public async Task OpenAsync(PlacementMapCardViewModel map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        MapSurface.Cursor = Cursors.Cross;
        BreadcrumbText.Text = $"{map.ModeLabel} / {map.DisplayName.ToUpperInvariant()}";
        ViewSelector.ItemsSource = map.Images;
        ViewSelector.SelectedIndex = 0;
        MapSurface.Width = map.ImageWidth;
        MapSurface.Height = map.ImageHeight;
        PlacementMarkers.Width = map.ImageWidth;
        PlacementMarkers.Height = map.ImageHeight;
        _mapFitMode = true;
        QueueFitMapToViewport();
        try
        {
            await _session.OpenAsync(map.Reference.Definition, map.ImageWidth, map.ImageHeight);
            RefreshRoutes();
            RefreshWorkspace();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppToastService.ShowError("SETUP LOAD FAILED", exception.Message);
        }
    }

    public Task FlushAsync() => _session.FlushAsync();

    private void RefreshRoutes()
    {
        if (_session.Document is null || _session.SelectedRoute is null) return;
        _refreshing = true;
        string selectedId = _session.SelectedRoute.Id;
        PlacementRouteRowViewModel[] rows = _session.Routes.Select(route => new PlacementRouteRowViewModel(
            route,
            route.IsShared ? "BASE" : _session.Document.Overrides.ContainsKey(route.Id) ? "CUSTOM" : "USES SHARED"))
            .ToArray();
        RouteSelector.ItemsSource = rows;
        RouteSelector.SelectedItem = rows.First(row => row.Id == selectedId);
        _refreshing = false;
        UpdateReset();
    }

    private void RefreshWorkspace()
    {
        if (_session.Document is null) return;
        PlacementMarkers.ItemsSource = PlacementStepRowFactory.Create(
                _session.CurrentRoute,
                _map?.ImageWidth ?? _session.Document.ImageWidth,
                _map?.ImageHeight ?? _session.Document.ImageHeight)
            .Where(row => row.IsPlacement)
            .ToArray();
        _timelinePanel.Load(_session);
        UpdateReset();
    }

    private void UpdateReset()
    {
        ResetButton.IsEnabled = _session.CanReset;
    }

    private async Task RunEditAsync(Func<Task> edit)
    {
        try
        {
            await edit();
            RefreshRoutes();
            RefreshWorkspace();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or InvalidOperationException)
        {
            _timelinePanel.ShowError(exception.Message);
            RefreshRoutes();
            RefreshWorkspace();
        }
    }

    private void BackToMaps_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        CloseTimelineWindow();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RouteSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_refreshing || RouteSelector.SelectedItem is not PlacementRouteRowViewModel row) return;
        _session.SelectRoute(row.Id);
        RefreshWorkspace();
    }

    private async void Reset_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!_session.CanReset) return;
        await RunEditAsync(_session.ResetAsync);
    }

    private void TimelinePanel_OnSetupChanged(object? sender, EventArgs eventArgs)
    {
        RefreshRoutes();
        PlacementMarkers.ItemsSource = PlacementStepRowFactory.Create(
                _session.CurrentRoute,
                _map?.ImageWidth ?? _session.Document?.ImageWidth ?? 1366,
                _map?.ImageHeight ?? _session.Document?.ImageHeight ?? 700)
            .Where(row => row.IsPlacement)
            .ToArray();
    }

    private void TimelinePanel_OnPopOutRequested(object? sender, EventArgs eventArgs)
    {
        if (_timelineWindow is null) PopOutTimeline();
        else _timelineWindow.Close();
    }

    private void PopOutTimeline()
    {
        TimelineHost.Content = null;
        TimelineHost.Visibility = Visibility.Collapsed;

        PlacementTimelineWindow window = new(_timelinePanel)
        {
            Owner = Window.GetWindow(this),
        };
        window.Closed += TimelineWindow_OnClosed;
        _timelineWindow = window;
        _timelinePanel.SetPoppedOut(true);
        window.Show();
    }

    private void TimelineWindow_OnClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not PlacementTimelineWindow window || !ReferenceEquals(window, _timelineWindow)) return;
        window.Closed -= TimelineWindow_OnClosed;
        window.DetachTimeline();
        _timelineWindow = null;
        TimelineHost.Visibility = Visibility.Visible;
        TimelineHost.Content = _timelinePanel;
        _timelinePanel.SetPoppedOut(false);
    }

    private void CloseTimelineWindow()
    {
        _timelineWindow?.Close();
    }

    private async void MapSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_map is null || !_session.CanEdit) return;
        Point point = eventArgs.GetPosition(MapSurface);
        int x = (int)Math.Round(Math.Clamp(point.X, 0, _map.ImageWidth - 1));
        int y = (int)Math.Round(Math.Clamp(point.Y, 0, _map.ImageHeight - 1));
        await RunEditAsync(() => _session.AddPlacementAsync(x, y));
        eventArgs.Handled = true;
    }

    private void MapViewport_OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (_mapFitMode) FitMapToViewport();
    }

    private void MapViewport_OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
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

    private void MapViewport_OnLostMouseCapture(object sender, MouseEventArgs eventArgs) =>
        EndMapPan(releaseCapture: false);

    private void EndMapPan(bool releaseCapture = true)
    {
        _mapPanStart = null;
        MapViewport.Cursor = null;
        if (releaseCapture && Mouse.Captured == MapViewport) MapViewport.ReleaseMouseCapture();
    }

    private void FitMapToViewport()
    {
        if (_map is null || MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0) return;
        double availableWidth = Math.Max(1, MapViewport.ActualWidth - 4);
        double availableHeight = Math.Max(1, MapViewport.ActualHeight - 4);
        double fit = Math.Min(availableWidth / _map.ImageWidth, availableHeight / _map.ImageHeight);
        SetMapZoom(fit, fitMode: true);
    }

    private void QueueFitMapToViewport() => _ = Dispatcher.BeginInvoke(FitMapToViewport);

    private void SetMapZoom(double value, bool fitMode, Point? anchor = null)
    {
        double next = Math.Clamp(value, 0.1, 6);
        Point viewportAnchor = anchor ?? new Point(MapViewport.ViewportWidth / 2, MapViewport.ViewportHeight / 2);
        double logicalX = (MapViewport.HorizontalOffset + viewportAnchor.X) / _mapZoom;
        double logicalY = (MapViewport.VerticalOffset + viewportAnchor.Y) / _mapZoom;
        _mapFitMode = fitMode;
        _mapZoom = next;
        MapScale.ScaleX = next;
        MapScale.ScaleY = next;
        _ = Dispatcher.BeginInvoke(() =>
        {
            MapViewport.ScrollToHorizontalOffset(logicalX * next - viewportAnchor.X);
            MapViewport.ScrollToVerticalOffset(logicalY * next - viewportAnchor.Y);
        });
    }

    private void ViewSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (ViewSelector.SelectedItem is not PlacementReferenceImageViewModel image) return;
        try
        {
            MapImage.Source = LoadImage(image.Path);
            if (_mapFitMode) QueueFitMapToViewport();
        }
        catch (IOException)
        {
            MapImage.Source = null;
        }
    }

    private static BitmapImage LoadImage(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
