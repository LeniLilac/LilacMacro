using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlacementEditorView : UserControl
{
    private readonly PlacementEditorSession _session;
    private readonly PlacementTimelinePanel _timelinePanel;
    private readonly PlacementUnitSelector _unitSelector;
    private PlacementTimelineWindow? _timelineWindow;
    private PlacementMapCardViewModel? _map;
    private IReadOnlyList<PlacementMapCardViewModel> _availableMaps = [];
    private bool _refreshing;
    private bool _refreshingDefaults;
    private double _mapZoom = 1;
    private bool _mapFitMode = true;
    private Point? _mapPanStart;
    private double _mapPanHorizontalOffset;
    private double _mapPanVerticalOffset;
    private readonly PlacementWheelGesturePolicy _wheelGesture = new();
    private PlacementSetupTestService? _testService;
    private CancellationTokenSource? _testCancellation;
    private Task<int>? _testTask;
    public PlacementEditorView()
    {
        InitializeComponent();
        _unitSelector = new PlacementUnitSelector(
            Unit1Button,
            Unit2Button,
            Unit3Button,
            Unit4Button,
            Unit5Button,
            Unit6Button);
        TeamSelector.ItemsSource = Enumerable.Range(1, 8)
            .Select(team => new PlacementNumberOption(team, $"TEAM {team}"))
            .ToArray();
        string placementRoot = Path.Combine(MacroInstanceContext.Current.ConfigurationRoot, "placements");
        _session = new PlacementEditorSession(new PlacementSetupStore(placementRoot));
        _timelinePanel = new PlacementTimelinePanel();
        _timelinePanel.SetupChanged += TimelinePanel_OnSetupChanged;
        _timelinePanel.PopOutRequested += TimelinePanel_OnPopOutRequested;
        _timelinePanel.TestSetupRequested += TimelinePanel_OnTestSetupRequested;
        TimelineHost.Content = _timelinePanel;
    }

    public event EventHandler? BackRequested;
    public bool IsTestRunning => _testTask is not null;

    internal void ConfigureSetupTest(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState)
    {
        if (_testService is not null)
            throw new InvalidOperationException("Setup test services are already configured.");
        _testService = new PlacementSetupTestService(deepDebug, ownerState);
    }

    public async Task OpenAsync(
        PlacementMapCardViewModel map,
        IReadOnlyList<PlacementMapCardViewModel>? availableMaps = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _availableMaps = availableMaps ?? [map];
        ApplyCursorModeCursor();
        BreadcrumbText.Text = $"{map.ModeLabel} / {map.DisplayName.ToUpperInvariant()}";
        ViewSelector.ItemsSource = map.Images;
        ViewSelector.SelectedIndex = 0;
        MapSurface.Width = map.ImageWidth;
        MapSurface.Height = map.ImageHeight;
        PlacementMarkers.Width = map.ImageWidth;
        PlacementMarkers.Height = map.ImageHeight;
        UnitPaletteOffset.X = 12;
        UnitPaletteOffset.Y = 12;
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
    public void CancelTest() => _testCancellation?.Cancel();

    public bool TryCancelTest()
    {
        if (!IsTestRunning) return false;
        CancelTest();
        return true;
    }

    public async Task CompleteForCloseAsync()
    {
        CancelTest();
        if (_testTask is not null)
        {
            try
            {
                await _testTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        await _session.FlushAsync();
        CloseTimelineWindow();
        _testService?.Dispose();
        _testService = null;
    }

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
        RefreshPlacementMarkers();
        _timelinePanel.Load(_session);
        RefreshAuthoringDefaults();
        UpdateReset();
    }

    private void RefreshAuthoringDefaults()
    {
        if (_session.Document is null) return;
        _refreshingDefaults = true;
        try
        {
            TeamSelector.SelectedValue = _session.CurrentRoute.TeamSlot;
            _unitSelector.Select(_session.CurrentRoute.SelectedUnitSlot);
        }
        finally
        {
            _refreshingDefaults = false;
        }
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
        RefreshPlacementMarkers();
    }

    private void TimelinePanel_OnPopOutRequested(object? sender, EventArgs eventArgs)
    {
        if (_timelineWindow is null) PopOutTimeline();
        else _timelineWindow.Close();
    }

    private async void TimelinePanel_OnTestSetupRequested(object? sender, EventArgs eventArgs)
    {
        if (_testTask is not null)
        {
            CancelTest();
            return;
        }
        if (_testService is null)
        {
            AppToastService.ShowError("SETUP TEST UNAVAILABLE", "Setup test services are not configured.");
            return;
        }

        try
        {
            await _session.FlushAsync();
            (PlacementSetupDocument document, PlacementRouteSetup route) = _session.CreatePlaybackSnapshot();
            _testCancellation = new CancellationTokenSource();
            SetTestRunning(true);
            _testTask = _testService.RunAsync(
                document,
                route,
                SetTestStatus,
                _testCancellation.Token);
            int executed = await _testTask;
            SetTestStatus($"COMPLETE - {executed} STEPS");
        }
        catch (OperationCanceledException)
        {
            SetTestStatus("STOPPED");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or InvalidOperationException or ArgumentException)
        {
            AppToastService.ShowError("SETUP TEST FAILED", exception.Message);
            SetTestStatus("FAILED");
        }
        finally
        {
            _testTask = null;
            _testCancellation?.Dispose();
            _testCancellation = null;
            SetTestRunning(false);
        }
    }

    private void SetTestStatus(string status) =>
        _ = Dispatcher.BeginInvoke(() => _timelinePanel.SetTestStatus(status));

    private void SetTestRunning(bool running)
    {
        RouteSelector.IsEnabled = !running;
        TeamSelector.IsEnabled = !running;
        UnitPalette.IsEnabled = !running;
        ViewSelector.IsEnabled = !running;
        BackToMapsButton.IsEnabled = !running;
        CopySetupButton.IsEnabled = !running;
        ResetButton.IsEnabled = !running && _session.CanReset;
        MapSurface.IsHitTestVisible = !running;
        _timelinePanel.SetTestState(running, running ? "STARTING" : null);
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

    private async void PlacementEditor_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (_session.Document is null || !_session.CanEdit || IsTestRunning ||
            PlacementUnitSlotShortcut.IsBlockedByFocus(Keyboard.FocusedElement as DependencyObject))
        {
            return;
        }

        int? unitSlot = PlacementUnitSlotShortcut.Resolve(eventArgs.Key, Keyboard.Modifiers);
        if (unitSlot is null) return;
        eventArgs.Handled = true;
        _refreshingDefaults = true;
        try
        {
            _unitSelector.Select(unitSlot.Value);
        }
        finally
        {
            _refreshingDefaults = false;
        }
        await SaveAuthoringDefaultsAsync();
    }

    private async void TeamSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        await SaveAuthoringDefaultsAsync();

    private async void UnitButton_OnChecked(object sender, RoutedEventArgs eventArgs) =>
        await SaveAuthoringDefaultsAsync();

    private async Task SaveAuthoringDefaultsAsync()
    {
        if (_refreshingDefaults || _session.Document is null ||
            TeamSelector.SelectedValue is not int teamSlot)
        {
            return;
        }

        PlacementRouteSetup route = _session.CurrentRoute;
        await RunEditAsync(() => _session.SetRouteDefaultsAsync(
            teamSlot,
            _unitSelector.SelectedSlot,
            route.DefaultStepDelayMilliseconds,
            route.DefaultTargetingPriority,
            route.DefaultAutoUpgradePriority));
    }

    private void UnitPalette_OnDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        UnitPaletteOffset.X = Math.Clamp(
            UnitPaletteOffset.X + eventArgs.HorizontalChange,
            0,
            Math.Max(0, MapViewportHost.ActualWidth - UnitPalette.ActualWidth));
        UnitPaletteOffset.Y = Math.Clamp(
            UnitPaletteOffset.Y + eventArgs.VerticalChange,
            0,
            Math.Max(0, MapViewportHost.ActualHeight - UnitPalette.ActualHeight));
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
