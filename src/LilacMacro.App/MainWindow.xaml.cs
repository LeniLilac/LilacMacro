using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Views;
using LilacMacro.App.Workspace;
using LilacMacro.Windows;

namespace LilacMacro.App;

public partial class MainWindow : Window
{
    private const int TimedCaptureHotkeyId = 0x4C4D;
    private const int ManualCaptureHotkeyId = 0x4C4E;
    private readonly DeepDebugSessionService _deepDebug;
    private readonly WorkspaceController _workspace;
    private readonly OcrRunner _ocr;
    private readonly ToolShellProfile _profile;
    private readonly Dictionary<PageKind, IWorkspacePage> _pages;
    private readonly CapturePage? _capturePage;
    private readonly StoryWireTestPage? _wireTestPage;
    private readonly DebugKeySequenceCoordinator? _debugInput;
    private GlobalHotkeyRegistration? _timedCaptureHotkey;
    private GlobalHotkeyRegistration? _manualCaptureHotkey;
    private HwndSource? _windowSource;
    private PageKind _currentPage;
    private bool _closingAfterFlush;
    private bool _timedHotkeyCaptureStarting;
    private bool _manualHotkeyCaptureStarting;

    internal MainWindow(DeepDebugSessionService deepDebug, ToolShellKind kind)
    {
        _deepDebug = deepDebug;
        _workspace = new WorkspaceController(deepDebug);
        _ocr = new OcrRunner(deepDebug);
        _profile = ToolShellProfile.Create(kind);
        _currentPage = _profile.StartPage;
        InitializeComponent();
        Title = _profile.WindowTitle;
        ToolNameText.Text = _profile.DisplayName;
        _pages = [];
        if (kind == ToolShellKind.DatasetBuilder)
        {
            _capturePage = new CapturePage(_workspace, NavigateAsync, deepDebug);
            _wireTestPage = null;
            _debugInput = null;
            _pages[PageKind.Capture] = _capturePage;
            _pages[PageKind.Review] = new ReviewPage(_workspace, _ocr);
            _pages[PageKind.Datasets] = new DatasetsPage(_workspace, NavigateAsync);
            _capturePage.CaptureStateChanged += CapturePage_OnCaptureStateChanged;
        }
        else
        {
            _capturePage = null;
            _debugInput = new DebugKeySequenceCoordinator(_workspace);
            _wireTestPage = new StoryWireTestPage(_workspace, _ocr, deepDebug);
            _pages[PageKind.Debug] = new DebugPage(_workspace, _ocr, _debugInput, deepDebug);
            _pages[PageKind.WireTest] = _wireTestPage;
            _debugInput.Changed += DebugInput_OnChanged;
        }
        ConfigureToolShell();
        _workspace.Changed += Workspace_OnChanged;
        _deepDebug.OptionsChanged += DeepDebug_OnOptionsChanged;
        _deepDebug.ArchiveSaved += DeepDebug_OnArchiveSaved;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
    }

    private void ConfigureToolShell()
    {
        CaptureNav.Visibility = _profile.Includes(PageKind.Capture) ? Visibility.Visible : Visibility.Collapsed;
        ReviewNav.Visibility = _profile.Includes(PageKind.Review) ? Visibility.Visible : Visibility.Collapsed;
        DatasetsNav.Visibility = _profile.Includes(PageKind.Datasets) ? Visibility.Visible : Visibility.Collapsed;
        DebugNav.Visibility = _profile.Includes(PageKind.Debug) ? Visibility.Visible : Visibility.Collapsed;
        WireTestNav.Visibility = _profile.Includes(PageKind.WireTest) ? Visibility.Visible : Visibility.Collapsed;
        DatasetPill.Visibility = _profile.Kind == ToolShellKind.DatasetBuilder
            ? Visibility.Visible
            : Visibility.Collapsed;
        ManualCaptureKeyPill.Visibility = _capturePage is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void DeepDebugToggle_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await _deepDebug.UpdateOptionsAsync(
            !_deepDebug.Options.Enabled,
            _deepDebug.Options.FrameRetentionMinutes);
    }

    private void DeepDebug_OnOptionsChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.Invoke(UpdateDeepDebugStatus);

    private void DeepDebug_OnArchiveSaved(object? sender, string path) =>
        Dispatcher.Invoke(() =>
        {
            DeepDebugPill.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
            DeepDebugPillText.Text = $"DEBUG SAVED {Path.GetFileNameWithoutExtension(path)}";
        });

    private void UpdateDeepDebugStatus()
    {
        DeepDebugPill.SetResourceReference(
            Border.BackgroundProperty,
            _deepDebug.Options.Enabled ? "AccentBrush" : "MutedBrush");
        DeepDebugPillText.Text = _deepDebug.Options.Enabled
            ? $"DEEP DEBUG {_deepDebug.Options.FrameRetentionMinutes}M"
            : "DEEP DEBUG OFF";
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            await _workspace.InitializeAsync();
            UpdateDeepDebugStatus();
            RegisterToolHotkeys();
            await NavigateAsync(_profile.StartPage);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "LilacMacro startup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegisterToolHotkeys()
    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            _windowSource = HwndSource.FromHwnd(handle)
                ?? throw new InvalidOperationException("Could not attach the capture key to LilacMacro.");
            _windowSource.AddHook(WindowMessageHook);
            if (_capturePage is not null) RegisterManualCaptureHotkey(handle);
            RegisterTimedCaptureHotkey(handle);
        }
        catch (Exception)
        {
            if (_capturePage is not null)
            {
                ManualCaptureKeyPill.SetResourceReference(Border.BackgroundProperty, "DangerBrush");
                ManualCaptureKeyPillText.Text = "F5 UNAVAILABLE";
            }
            CaptureKeyPill.SetResourceReference(Border.BackgroundProperty, "DangerBrush");
            CaptureKeyPillText.Text = "F6 UNAVAILABLE";
        }
    }

    private void RegisterManualCaptureHotkey(nint handle)
    {
        try
        {
            _manualCaptureHotkey = new GlobalHotkeyRegistration(
                handle,
                ManualCaptureHotkeyId,
                GlobalHotkeyRegistration.F5VirtualKey);
            UpdateManualCaptureKeyStatus();
        }
        catch (Exception)
        {
            ManualCaptureKeyPill.SetResourceReference(Border.BackgroundProperty, "DangerBrush");
            ManualCaptureKeyPillText.Text = "F5 UNAVAILABLE";
        }
    }

    private void RegisterTimedCaptureHotkey(nint handle)
    {
        try
        {
            _timedCaptureHotkey = new GlobalHotkeyRegistration(
                handle,
                TimedCaptureHotkeyId,
                GlobalHotkeyRegistration.F6VirtualKey);
            UpdateTimedCaptureKeyStatus();
        }
        catch (Exception)
        {
            CaptureKeyPill.SetResourceReference(Border.BackgroundProperty, "DangerBrush");
            CaptureKeyPillText.Text = "F6 UNAVAILABLE";
        }
    }

    private nint WindowMessageHook(nint window, int message, nint parameter, nint data, ref bool handled)
    {
        if (_capturePage is not null && _manualCaptureHotkey?.Matches(message, parameter) == true)
        {
            handled = true;
            if (_capturePage.CanCaptureManualFrame &&
                !_manualHotkeyCaptureStarting &&
                _wireTestPage?.IsRunning != true)
            {
                _ = RunManualFrameHotkeyAsync();
            }
            return 0;
        }
        if (_timedCaptureHotkey?.Matches(message, parameter) == true)
        {
            handled = true;
            if (_debugInput?.HandleF6() == true)
            {
                UpdateTimedCaptureKeyStatus();
                return 0;
            }
            if (_capturePage is not null &&
                !_capturePage.IsCapturing &&
                !_capturePage.IsManualSessionActive &&
                _wireTestPage?.IsRunning != true &&
                !_timedHotkeyCaptureStarting)
            {
                _ = RunTimedCaptureHotkeyAsync();
            }
        }
        return 0;
    }

    private async Task RunTimedCaptureHotkeyAsync()
    {
        CapturePage capturePage = _capturePage
            ?? throw new InvalidOperationException("Timed capture is not available in Runtime Lab.");
        _timedHotkeyCaptureStarting = true;
        try
        {
            await FlushReviewAsync();
            await capturePage.CaptureFromHotkeyAsync();
        }
        finally
        {
            _timedHotkeyCaptureStarting = false;
            UpdateTimedCaptureKeyStatus();
        }
    }

    private async Task RunManualFrameHotkeyAsync()
    {
        CapturePage capturePage = _capturePage
            ?? throw new InvalidOperationException("Manual capture is not available in Runtime Lab.");
        _manualHotkeyCaptureStarting = true;
        try
        {
            await FlushReviewAsync();
            bool captured = await capturePage.CaptureManualFrameFromHotkeyAsync();
            if (captured && _currentPage == PageKind.Review && _pages[PageKind.Review] is ReviewPage review)
            {
                await review.RefreshAsync();
            }
        }
        finally
        {
            _manualHotkeyCaptureStarting = false;
            UpdateManualCaptureKeyStatus();
        }
    }

    private Task FlushReviewAsync() =>
        _currentPage == PageKind.Review && _pages[PageKind.Review] is ReviewPage review
            ? review.FlushPendingAsync()
            : Task.CompletedTask;

    private void CapturePage_OnCaptureStateChanged(object? sender, EventArgs eventArgs)
    {
        UpdateTimedCaptureKeyStatus();
        UpdateManualCaptureKeyStatus();
    }

    private void DebugInput_OnChanged(object? sender, EventArgs eventArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateTimedCaptureKeyStatus);
            return;
        }
        UpdateTimedCaptureKeyStatus();
    }

    private void UpdateTimedCaptureKeyStatus()
    {
        if (_timedCaptureHotkey is null) return;
        if (_debugInput?.OwnsF6 == true)
        {
            CaptureKeyPill.SetResourceReference(
                Border.BackgroundProperty,
                _debugInput.State == DebugKeySequenceState.Running ? "AccentBrush" : "YellowBrush");
            CaptureKeyPillText.Text = _debugInput.State switch
            {
                DebugKeySequenceState.Arming => "F6 FOCUSING",
                DebugKeySequenceState.Armed => "F6 ARMED",
                DebugKeySequenceState.Running => "F6 KEYS",
                _ => "F6 STOPPING",
            };
            return;
        }
        if (_capturePage is null)
        {
            CaptureKeyPill.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
            CaptureKeyPillText.Text = "F6 READY";
            return;
        }
        CaptureKeyPill.SetResourceReference(Border.BackgroundProperty, _capturePage.TimedCaptureState switch
        {
            CaptureRunState.Capturing => "AccentBrush",
            CaptureRunState.Complete => "SuccessBrush",
            CaptureRunState.Failed => "DangerBrush",
            CaptureRunState.Cancelled => "YellowBrush",
            _ => "MutedBrush",
        });
        CaptureKeyPillText.Text = _capturePage.TimedCaptureState switch
        {
            CaptureRunState.Capturing => "F6 CAPTURING",
            CaptureRunState.Complete => "F6 COMPLETE",
            CaptureRunState.Failed => "F6 FAILED",
            CaptureRunState.Cancelled => "F6 CANCELLED",
            _ => "F6 READY",
        };
    }

    private void UpdateManualCaptureKeyStatus()
    {
        if (_manualCaptureHotkey is null || _capturePage is null) return;
        if (_capturePage.ManualCaptureState == CaptureRunState.Capturing)
        {
            ManualCaptureKeyPill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            ManualCaptureKeyPillText.Text = "F5 CAPTURING";
            return;
        }
        if (!_capturePage.IsManualSessionActive)
        {
            ManualCaptureKeyPill.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
            ManualCaptureKeyPillText.Text = "F5 IDLE";
            return;
        }
        ManualCaptureKeyPill.SetResourceReference(
            Border.BackgroundProperty,
            _capturePage.ManualCaptureState == CaptureRunState.Failed ? "DangerBrush" : "SuccessBrush");
        ManualCaptureKeyPillText.Text = _capturePage.ManualCaptureState == CaptureRunState.Failed
            ? "F5 FAILED"
            : "F5 READY";
    }

    private async Task NavigateAsync(PageKind target)
    {
        if (!_pages.TryGetValue(target, out IWorkspacePage? page))
        {
            throw new InvalidOperationException($"{target} is not available in {_profile.DisplayName}.");
        }
        if (_currentPage == PageKind.Review && _pages[PageKind.Review] is ReviewPage review)
        {
            await review.FlushPendingAsync();
        }

        _currentPage = target;
        PageHost.Content = page;
        SetActiveNavigation(target);
        await page.RefreshAsync();
    }

    private void Workspace_OnChanged(object? sender, EventArgs eventArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateHeaderStatus);
            return;
        }
        UpdateHeaderStatus();
    }

    private void UpdateHeaderStatus()
    {
        if (_workspace.RobloxWindow is null)
        {
            RobloxPill.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
            RobloxPillText.Text = "ROBLOX: OFFLINE";
        }
        else if (_workspace.WindowIsReady)
        {
            RobloxPill.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
            RobloxPillText.Text = $"ROBLOX: {_workspace.TargetSize}";
        }
        else
        {
            RobloxPill.SetResourceReference(Border.BackgroundProperty, "YellowBrush");
            RobloxPillText.Text = $"ROBLOX: {_workspace.ObservedClientSize}";
        }

        if (_workspace.ActiveDataset is { } active)
        {
            DatasetPillText.Text = active.Manifest.IsFinalized
                ? $"DATASET: {active.Manifest.Name.ToUpperInvariant()}"
                : $"DRAFT: {active.Manifest.Frames.Count} FRAMES";
        }
        else
        {
            DatasetPillText.Text = "NO DATASET";
        }
        UpdateTimedCaptureKeyStatus();
        UpdateManualCaptureKeyStatus();
    }

    private void SetActiveNavigation(PageKind active)
    {
        CaptureNav.Style = (Style)FindResource(active == PageKind.Capture ? "NavButtonActiveStyle" : "NavButtonStyle");
        ReviewNav.Style = (Style)FindResource(active == PageKind.Review ? "NavButtonActiveStyle" : "NavButtonStyle");
        DatasetsNav.Style = (Style)FindResource(active == PageKind.Datasets ? "NavButtonActiveStyle" : "NavButtonStyle");
        DebugNav.Style = (Style)FindResource(active == PageKind.Debug ? "NavButtonActiveStyle" : "NavButtonStyle");
        WireTestNav.Style = (Style)FindResource(active == PageKind.WireTest ? "NavButtonActiveStyle" : "NavButtonStyle");
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_closingAfterFlush) return;
        eventArgs.Cancel = true;
        try
        {
            if (_pages.TryGetValue(PageKind.Review, out IWorkspacePage? page) && page is ReviewPage review)
                await review.FlushPendingAsync();
            if (_capturePage is not null) await _capturePage.CompleteForCloseAsync();
            if (_wireTestPage is not null) await _wireTestPage.StopAsync();
            if (_debugInput is not null) await _debugInput.StopAsync();
        }
        finally
        {
            _closingAfterFlush = true;
            if (_windowSource is not null) _windowSource.RemoveHook(WindowMessageHook);
            _manualCaptureHotkey?.Dispose();
            _timedCaptureHotkey?.Dispose();
            _ocr.Dispose();
            _workspace.Dispose();
            _deepDebug.OptionsChanged -= DeepDebug_OnOptionsChanged;
            _deepDebug.ArchiveSaved -= DeepDebug_OnArchiveSaved;
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left) return;
        if (eventArgs.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private async void CaptureNav_OnClick(object sender, RoutedEventArgs eventArgs) => await NavigateAsync(PageKind.Capture);

    private async void ReviewNav_OnClick(object sender, RoutedEventArgs eventArgs) => await NavigateAsync(PageKind.Review);

    private async void DatasetsNav_OnClick(object sender, RoutedEventArgs eventArgs) => await NavigateAsync(PageKind.Datasets);

    private async void DebugNav_OnClick(object sender, RoutedEventArgs eventArgs) => await NavigateAsync(PageKind.Debug);

    private async void WireTestNav_OnClick(object sender, RoutedEventArgs eventArgs) => await NavigateAsync(PageKind.WireTest);

    private void Minimize_OnClick(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void Maximize_OnClick(object sender, RoutedEventArgs eventArgs) => ToggleMaximize();

    private void Close_OnClick(object sender, RoutedEventArgs eventArgs) => Close();
}
