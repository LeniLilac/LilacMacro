using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LilacMacro.App.Notifications;
using LilacMacro.Windows;

namespace LilacMacro.App.Controls;

public partial class RobloxDockSurface : UserControl
{
    private readonly RobloxWindowService _windows = new();
    private readonly RobloxWindowDockService _dock;
    private readonly DispatcherTimer _refreshTimer;
    private Window? _owner;
    private bool _requested;
    private bool _dashboardActive = true;
    private bool _releaseForClose;
    private string _status = "READY · 1366 x 700";
    private string? _lastReportedError;

    public RobloxDockSurface()
    {
        InitializeComponent();
        _dock = new RobloxWindowDockService(_windows);
        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => RefreshDock(),
            Dispatcher);
    }

    public event EventHandler? DockStateChanged;

    public bool IsRequested => _requested;

    public bool IsDocked => _dock.IsDocked;

    public string Status => _status;

    public void SetRequested(bool requested)
    {
        _requested = requested;
        if (!requested)
        {
            if (!_dock.TryUndock(out string error)) ReportError(error);
            SetStatus("READY · 1366 x 700", "ROBLOX NOT DOCKED");
            return;
        }
        RefreshDock();
    }

    public bool SetDashboardActive(bool active, out string error)
    {
        _dashboardActive = active;
        if (!active && !_dock.TryUndock(out error)) return false;
        if (active) RefreshDock();
        error = string.Empty;
        return true;
    }

    public bool TryPrepareForClose(out string error)
    {
        _releaseForClose = true;
        _refreshTimer.Stop();
        if (_dock.TryUndock(out error)) return true;
        _releaseForClose = false;
        if (IsLoaded) _refreshTimer.Start();
        return false;
    }

    private void Surface_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _owner = Window.GetWindow(this);
        if (_owner is not null)
        {
            _owner.LocationChanged += Owner_OnGeometryChanged;
            _owner.SizeChanged += Owner_OnGeometryChanged;
            _owner.StateChanged += Owner_OnStateChanged;
            _owner.Activated += Owner_OnActivationChanged;
            _owner.Deactivated += Owner_OnActivationChanged;
        }
        UpdateTargetDipSize();
        _refreshTimer.Start();
        RefreshDock();
    }

    private void Surface_OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _refreshTimer.Stop();
        if (_owner is not null)
        {
            _owner.LocationChanged -= Owner_OnGeometryChanged;
            _owner.SizeChanged -= Owner_OnGeometryChanged;
            _owner.StateChanged -= Owner_OnStateChanged;
            _owner.Activated -= Owner_OnActivationChanged;
            _owner.Deactivated -= Owner_OnActivationChanged;
            _owner = null;
        }
        _ = _dock.TryUndock(out _);
    }

    private void Owner_OnGeometryChanged(object? sender, EventArgs eventArgs)
    {
        UpdateTargetDipSize();
        RefreshDock();
    }

    private void Owner_OnStateChanged(object? sender, EventArgs eventArgs) => RefreshDock();

    private void Owner_OnActivationChanged(object? sender, EventArgs eventArgs) =>
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(RefreshDock));

    private void UpdateTargetDipSize()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        DockTarget.Width = RobloxWindowDockService.ClientWidth / dpi.DpiScaleX;
        DockTarget.Height = RobloxWindowDockService.ClientHeight / dpi.DpiScaleY;
    }

    private void RefreshDock()
    {
        if (_releaseForClose || !_dashboardActive || !_requested || !IsLoaded) return;

        try
        {
            Window? owner = _owner ?? Window.GetWindow(this);
            if (owner is null || owner.WindowState == WindowState.Minimized ||
                !TryGetTargetLocation(owner, out int x, out int y))
            {
                _ = _dock.TrySuspend(out _);
                SetStatus("VIEW TOO SMALL", "MAXIMIZE TO DOCK");
                return;
            }

            bool isDocked = _dock.IsDocked;
            nint sourceHandle = _dock.SourceHandle;
            RobloxDockMaintenanceAction maintenance = RobloxDockMaintenancePolicy.Resolve(
                sourceHandle != nint.Zero,
                isDocked);
            RobloxWindow? source = maintenance == RobloxDockMaintenanceAction.Acquire
                ? _windows.FindBest()
                : null;
            nint ownerHandle = new WindowInteropHelper(owner).Handle;
            if (sourceHandle == nint.Zero)
            {
                source ??= _windows.FindBest();
                sourceHandle = source?.Handle ?? nint.Zero;
            }
            if (!_dock.IsDashboardExposed(ownerHandle, sourceHandle))
            {
                if (!_dock.TrySuspend(out string error)) ReportError(error);
                SetStatus("PAUSED", "DASHBOARD COVERED");
                return;
            }

            if (maintenance != RobloxDockMaintenanceAction.Acquire)
            {
                _dock.UpdateBounds(x, y);
            }
            else
            {
                source ??= _windows.FindBest();
                if (source is null)
                {
                    SetStatus("ROBLOX NOT FOUND", "OPEN ROBLOX");
                    return;
                }
                _dock.Dock(source.Value, x, y);
            }

            _lastReportedError = null;
            _status = "DOCKED · 1366 x 700";
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            DockStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            _ = _dock.TryUndock(out _);
            SetStatus("DOCK FAILED", "ROBLOX NOT DOCKED");
            ReportError(exception.Message);
        }
    }

    private bool TryGetTargetLocation(Window owner, out int x, out int y)
    {
        Point targetTopLeft = DockTarget.PointToScreen(new Point(0, 0));
        Point targetBottomRight = DockTarget.PointToScreen(new Point(DockTarget.ActualWidth, DockTarget.ActualHeight));
        Point ownerTopLeft = owner.PointToScreen(new Point(0, 0));
        Point ownerBottomRight = owner.PointToScreen(new Point(owner.ActualWidth, owner.ActualHeight));
        x = checked((int)Math.Round(targetTopLeft.X));
        y = checked((int)Math.Round(targetTopLeft.Y));
        int right = checked((int)Math.Round(targetBottomRight.X));
        int bottom = checked((int)Math.Round(targetBottomRight.Y));
        return x >= Math.Floor(ownerTopLeft.X) &&
            y >= Math.Floor(ownerTopLeft.Y) &&
            right <= Math.Ceiling(ownerBottomRight.X) &&
            bottom <= Math.Ceiling(ownerBottomRight.Y) &&
            Math.Abs((right - x) - RobloxWindowDockService.ClientWidth) <= 1 &&
            Math.Abs((bottom - y) - RobloxWindowDockService.ClientHeight) <= 1;
    }

    private void SetStatus(string status, string placeholder)
    {
        _status = status;
        PlaceholderTitle.Text = placeholder;
        PlaceholderPanel.Visibility = Visibility.Visible;
        DockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportError(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || string.Equals(message, _lastReportedError, StringComparison.Ordinal)) return;
        _lastReportedError = message;
        AppToastService.ShowError("ROBLOX DOCK FAILED", message);
    }
}
