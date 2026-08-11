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

namespace LilacMacro.App;

public partial class MacroShellWindow : Window
{
    private readonly Dictionary<MacroShellPage, UserControl> _pages;
    private readonly MacroOwnerState _ownerState;
    private readonly MacroDashboardPage _macroPage;
    private readonly PlacementSetupPage _setupPage;
    private readonly LocalInstanceManagerController _instanceManager = new();
    private readonly WindowShutdownState _shutdown = new();
    private readonly DispatcherTimer _toastTimer;
    private MacroShellPage _currentPage;

    internal MacroShellWindow(DeepDebugSessionService deepDebug, MacroOwnerState ownerState)
    {
        InitializeComponent();
        InstanceNameText.Text = MacroInstanceContext.Current.DisplayName.ToUpperInvariant();
        Title = $"LilacMacro — {MacroInstanceContext.Current.DisplayName}";
        InitializeWindowSizing();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _toastTimer.Tick += ToastTimer_OnTick;
        AppToastService.ErrorRaised += AppToastService_OnErrorRaised;
        _ownerState = ownerState;
        _macroPage = new MacroDashboardPage(deepDebug, _ownerState);
        _setupPage = new PlacementSetupPage(deepDebug, _ownerState);
        _pages = new Dictionary<MacroShellPage, UserControl>
        {
            [MacroShellPage.Macro] = _macroPage,
            [MacroShellPage.Plan] = new PlanPage(_ownerState),
            [MacroShellPage.Setup] = _setupPage,
            [MacroShellPage.Settings] = new SettingsPage(deepDebug, _ownerState, _instanceManager, SetMacroHotkeyCaptureSuspended),
        };
        InitializeMacroHotkey();
        Closing += MacroShellWindow_OnClosing;
        Closed += MacroShellWindow_OnClosed;
        Navigate(MacroShellPage.Macro);
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
            _setupPage.PrepareForClose();
            await _macroPage.CompleteForCloseAsync();
            await _setupPage.CompleteForCloseAsync();
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

    private void AppToastService_OnErrorRaised(object? sender, AppErrorToast toast)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ShowToast(toast));
            return;
        }
        ShowToast(toast);
    }

    private void ShowToast(AppErrorToast toast)
    {
        ErrorToastTitle.Text = toast.Title.ToUpperInvariant();
        ErrorToastMessage.Text = toast.Message;
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
        _toastTimer.Stop();
        AppToastService.ErrorRaised -= AppToastService_OnErrorRaised;
    }
}
