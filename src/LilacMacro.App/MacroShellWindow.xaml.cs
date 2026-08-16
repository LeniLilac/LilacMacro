using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LilacMacro.App.Lifecycle;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Notifications;
using LilacMacro.App.Views;
using LilacMacro.App.Workspace;
using LilacMacro.App.Runtime;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Updates;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;
using LilacMacro.Windows;

namespace LilacMacro.App;

public partial class MacroShellWindow : Window
{
    private readonly Dictionary<MacroShellPage, UserControl> _pages;
    private readonly MacroOwnerState _ownerState;
    private readonly MacroDashboardPage _macroPage;
    private readonly PlacementSetupPage _setupPage;
    private readonly LocalInstanceManagerController _instanceManager = new();
    private readonly ApplicationUpdateService _updates = new();
    private readonly SettingsPage _settingsPage;
    private readonly WindowShutdownState _shutdown = new();
    private readonly DispatcherTimer _toastTimer;
    private readonly ControlSnapshotTransport _controlTransport = new();
    private readonly DiagnosticUploadTransport _diagnosticUploads = new();
    private readonly ProductTelemetryTransport _telemetryTransport = new();
    private readonly ProductTelemetryService _telemetry;
    private readonly AutomaticDiagnosticReportService _automaticReports;
    private readonly ControlSnapshotPollingService _control;
    private readonly CancellationTokenSource _controlCancellation = new();
    private Task? _controlTask;
    private MacroShellPage _currentPage;
    private bool _minimizedForRun;

    internal MacroShellWindow(DeepDebugSessionService deepDebug, MacroOwnerState ownerState)
    {
        InitializeComponent();
        InstanceNameText.Text = MacroInstanceContext.Current.DisplayName.ToUpperInvariant();
        Title = $"LilacMacro — {MacroInstanceContext.Current.DisplayName}";
        InitializeWindowSizing();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _toastTimer.Tick += ToastTimer_OnTick;
        AppToastService.Raised += AppToastService_OnRaised;
        _ownerState = ownerState;
        _control = new ControlSnapshotPollingService(
            _controlTransport,
            new ControlSnapshotStore(
                Path.Combine(
                    MacroInstanceContext.Current.ConfigurationRoot,
                    "services",
                    "control.json"),
                new ControlSnapshotVerifier(ControlSnapshotTrust.PublicKeys)));
        DiagnosticInstallationStore installation = new(MacroInstanceContext.Current.ConfigurationRoot);
        _telemetry = new ProductTelemetryService(
            deepDebug, ownerState, installation, _telemetryTransport);
        _automaticReports = new AutomaticDiagnosticReportService(
            deepDebug, ownerState, installation, _diagnosticUploads);
        _macroPage = new MacroDashboardPage(deepDebug, _ownerState, _control);
        _macroPage.RunningChanged += MacroPage_OnRunningChanged;
        _setupPage = new PlacementSetupPage(deepDebug, _ownerState);
        _settingsPage = new SettingsPage(
            deepDebug,
            _ownerState,
            _instanceManager,
            _updates,
            _diagnosticUploads,
            SetMacroHotkeyCaptureSuspended);
        _pages = new Dictionary<MacroShellPage, UserControl>
        {
            [MacroShellPage.Macro] = _macroPage,
            [MacroShellPage.Plan] = new PlanPage(_ownerState),
            [MacroShellPage.Setup] = _setupPage,
            [MacroShellPage.Settings] = _settingsPage,
        };
        InitializeMacroHotkey();
        _ownerState.DisplayOptionsChanged += OwnerState_OnDisplayOptionsChanged;
        ApplyDisplayOptions(resize: false);
        Loaded += MacroShellWindow_OnLoaded;
        Closing += MacroShellWindow_OnClosing;
        Closed += MacroShellWindow_OnClosed;
        Navigate(MacroShellPage.Macro);
    }

    private async void MacroShellWindow_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _telemetry.Start();
        _controlTask ??= RunControlPollingAsync();
        await _settingsPage.CheckOnStartupAsync();
        await _macroPage.EnsureOcrReadyAsync();
        if (_ownerState.EffectiveMinimizeBehavior == MacroMinimizeBehavior.OnApplicationStart)
            WindowState = WindowState.Minimized;
    }

    private void OwnerState_OnDisplayOptionsChanged(object? sender, EventArgs eventArgs) =>
        ApplyDisplayOptions(resize: true);

    private void ApplyDisplayOptions(bool resize)
    {
        MacroLayoutProfile layout = EffectiveLayoutProfile();
        _macroPage.ApplyLayoutProfile(layout);
        ApplyWorkspaceSize(layout, resize);
    }

    private void MacroPage_OnRunningChanged(bool running)
    {
        MacroMinimizeBehavior minimize = MacroDisplayPolicy.EffectiveMinimizeBehavior(
            EffectiveLayoutProfile(),
            _ownerState.MinimizeBehavior);
        if (running && minimize == MacroMinimizeBehavior.WhileRunning)
        {
            _minimizedForRun = WindowState != WindowState.Minimized;
            WindowState = WindowState.Minimized;
        }
        else if (!running && _minimizedForRun)
        {
            _minimizedForRun = false;
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private MacroLayoutProfile EffectiveLayoutProfile()
    {
        if (!MacroInstanceContext.Current.IsManagedRunner) return _ownerState.LayoutProfile;
        (int width, int height) = WindowsDesktopMetrics.PrimaryDisplaySize();
        return MacroDisplayPolicy.ManagedViewportLayout(width, height);
    }

    private void Navigate(MacroShellPage target)
    {
        if (_currentPage == MacroShellPage.Setup &&
            target != MacroShellPage.Setup &&
            !_setupPage.TryDeactivate(out string setupError))
        {
            AppToastService.ShowError("SETUP TEST RUNNING", setupError);
            return;
        }
        if (_currentPage == MacroShellPage.Macro &&
            target != MacroShellPage.Macro &&
            !_macroPage.SetDashboardActive(false, out string error))
        {
            AppToastService.ShowError("ROBLOX UNDOCK FAILED", error);
            return;
        }
        _currentPage = target;
        PageHost.Content = _pages[target];
        if (target == MacroShellPage.Macro &&
            !_macroPage.SetDashboardActive(true, out error))
        {
            AppToastService.ShowError("ROBLOX DOCK FAILED", error);
        }
        MacroTab.Style = TabStyle(MacroShellPage.Macro);
        PlanTab.Style = TabStyle(MacroShellPage.Plan);
        SetupTab.Style = TabStyle(MacroShellPage.Setup);
        SettingsTab.Style = TabStyle(MacroShellPage.Settings);
    }

    private Style TabStyle(MacroShellPage tab) =>
        (Style)FindResource(tab == _currentPage ? "BrowserTabActiveStyle" : "BrowserTabStyle");

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left || IsInsideButton(eventArgs.OriginalSource)) return;
        if (eventArgs.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private static bool IsInsideButton(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private void MacroTab_OnClick(object sender, RoutedEventArgs eventArgs) => Navigate(MacroShellPage.Macro);

    private void PlanTab_OnClick(object sender, RoutedEventArgs eventArgs) => Navigate(MacroShellPage.Plan);

    private void SetupTab_OnClick(object sender, RoutedEventArgs eventArgs) => Navigate(MacroShellPage.Setup);

    private void SettingsTab_OnClick(object sender, RoutedEventArgs eventArgs) => Navigate(MacroShellPage.Settings);

    private void Minimize_OnClick(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void Maximize_OnClick(object sender, RoutedEventArgs eventArgs) => ToggleMaximize();

    private void Close_OnClick(object sender, RoutedEventArgs eventArgs) => Close();

    private async void MacroShellWindow_OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        WindowShutdownDecision decision = _shutdown.BeginClose();
        if (decision == WindowShutdownDecision.AllowClose) return;
        eventArgs.Cancel = true;
        if (decision == WindowShutdownDecision.CancelWhileFlushing) return;

        if (!_macroPage.TryPrepareForClose(out string dockError))
        {
            _shutdown.FailFlush();
            AppToastService.ShowError("ROBLOX UNDOCK FAILED", dockError);
            return;
        }

        try
        {
            _controlCancellation.Cancel();
            _settingsPage.CancelDiagnosticUpload();
            _setupPage.PrepareForClose();
            await _macroPage.CompleteForCloseAsync();
            await _setupPage.CompleteForCloseAsync();
            await CompleteControlPollingAsync();
            await _telemetry.DisposeAsync();
            await _automaticReports.DisposeAsync();
            await _ownerState.FlushAsync();
            _shutdown.CompleteFlush();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(Close));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _shutdown.FailFlush();
            AppToastService.ShowError("LOCAL SAVE FAILED", exception.Message);
        }
    }

    private void AppToastService_OnRaised(object? sender, AppToast toast)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ShowToast(toast));
            return;
        }
        ShowToast(toast);
    }

    private void ShowToast(AppToast toast)
    {
        ErrorToastTitle.Text = toast.Title.ToUpperInvariant();
        ErrorToastMessage.Text = toast.Message;
        ErrorToast.SetResourceReference(
            Border.BackgroundProperty,
            toast.Tone == AppToastTone.Success ? "SuccessBrush" : "DangerBrush");
        ErrorToastIcon.Data = (Geometry)FindResource(
            toast.Tone == AppToastTone.Success ? "Lucide.CircleCheck" : "Lucide.TriangleAlert");
        ErrorToast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ToastDismiss_OnClick(object sender, RoutedEventArgs eventArgs) => HideToast();

    private void ToastTimer_OnTick(object? sender, EventArgs eventArgs) => HideToast();

    private void HideToast()
    {
        _toastTimer.Stop();
        ErrorToast.Visibility = Visibility.Collapsed;
    }

    private void MacroShellWindow_OnClosed(object? sender, EventArgs eventArgs)
    {
        DisposeMacroHotkey();
        DisposeWindowSizing();
        _macroPage.RunningChanged -= MacroPage_OnRunningChanged;
        _ownerState.DisplayOptionsChanged -= OwnerState_OnDisplayOptionsChanged;
        _controlCancellation.Cancel();
        _controlCancellation.Dispose();
        _controlTransport.Dispose();
        _diagnosticUploads.Dispose();
        _telemetryTransport.Dispose();
        _updates.Dispose();
        _toastTimer.Stop();
        AppToastService.Raised -= AppToastService_OnRaised;
    }

    private async Task RunControlPollingAsync()
    {
        try
        {
            await _control.RunAsync(
                _ownerState.IsOnlineFeaturesDurablyEnabledAsync,
                _controlCancellation.Token);
        }
        catch (OperationCanceledException) when (_controlCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppToastService.ShowError("SERVICE STATUS PAUSED", exception.Message);
        }
    }

    private async Task CompleteControlPollingAsync()
    {
        if (_controlTask is null) return;
        await _controlTask;
        _controlTask = null;
    }
}
