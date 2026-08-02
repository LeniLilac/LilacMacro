using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Views;
using LilacMacro.App.Workspace;
using LilacMacro.Windows;

namespace LilacMacro.App;

public partial class MainWindow : Window
{
    private const int TimedCaptureHotkeyId = 0x4C4D;
    private const int ManualCaptureHotkeyId = 0x4C4E;
    private readonly WorkspaceController _workspace = new();
    private readonly OcrRunner _ocr = new();
    private readonly Dictionary<PageKind, IWorkspacePage> _pages;
    private readonly CapturePage _capturePage;
    private GlobalHotkeyRegistration? _timedCaptureHotkey;
    private GlobalHotkeyRegistration? _manualCaptureHotkey;
    private HwndSource? _windowSource;
    private PageKind _currentPage = PageKind.Capture;
    private bool _closingAfterFlush;
    private bool _timedHotkeyCaptureStarting;
    private bool _manualHotkeyCaptureStarting;

    public MainWindow()
    {
        InitializeComponent();
        _capturePage = new CapturePage(_workspace, NavigateAsync);
        _pages = new Dictionary<PageKind, IWorkspacePage>
        {
            [PageKind.Capture] = _capturePage,
            [PageKind.Review] = new ReviewPage(_workspace, _ocr),
            [PageKind.Datasets] = new DatasetsPage(_workspace, NavigateAsync),
        };
        _capturePage.CaptureStateChanged += CapturePage_OnCaptureStateChanged;
        _workspace.Changed += Workspace_OnChanged;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            await _workspace.InitializeAsync();
            RegisterCaptureHotkeys();
            await NavigateAsync(PageKind.Capture);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "LilacMacro startup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegisterCaptureHotkeys()
    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            _windowSource = HwndSource.FromHwnd(handle)
                ?? throw new InvalidOperationException("Could not attach the capture key to LilacMacro.");
            _windowSource.AddHook(WindowMessageHook);
            RegisterManualCaptureHotkey(handle);
            RegisterTimedCaptureHotkey(handle);
        }
        catch (Exception)
        {
            ManualCaptureKeyPill.Background = (Brush)FindResource("DangerBrush");
            ManualCaptureKeyPillText.Text = "F5 UNAVAILABLE";
            CaptureKeyPill.Background = (Brush)FindResource("DangerBrush");
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
            ManualCaptureKeyPill.Background = (Brush)FindResource("DangerBrush");
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
            CaptureKeyPill.Background = (Brush)FindResource("DangerBrush");
            CaptureKeyPillText.Text = "F6 UNAVAILABLE";
        }
    }

    private nint WindowMessageHook(nint window, int message, nint parameter, nint data, ref bool handled)
    {
        if (_manualCaptureHotkey?.Matches(message, parameter) == true)
        {
            handled = true;
            if (_capturePage.CanCaptureManualFrame && !_manualHotkeyCaptureStarting)
            {
                _ = RunManualFrameHotkeyAsync();
            }
            return 0;
        }
        if (_timedCaptureHotkey?.Matches(message, parameter) == true)
        {
            handled = true;
            if (!_capturePage.IsCapturing &&
                !_capturePage.IsManualSessionActive &&
                !_timedHotkeyCaptureStarting)
            {
                _ = RunTimedCaptureHotkeyAsync();
            }
        }
        return 0;
    }

    private async Task RunTimedCaptureHotkeyAsync()
    {
        _timedHotkeyCaptureStarting = true;
        try
        {
            await FlushReviewAsync();
            await _capturePage.CaptureFromHotkeyAsync();
        }
        finally
        {
            _timedHotkeyCaptureStarting = false;
            UpdateTimedCaptureKeyStatus();
        }
    }

    private async Task RunManualFrameHotkeyAsync()
    {
        _manualHotkeyCaptureStarting = true;
        try
        {
            await FlushReviewAsync();
            bool captured = await _capturePage.CaptureManualFrameFromHotkeyAsync();
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

    private void UpdateTimedCaptureKeyStatus()
    {
        if (_timedCaptureHotkey is null) return;
        CaptureKeyPill.Background = (Brush)FindResource(_capturePage.TimedCaptureState switch
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
        if (_manualCaptureHotkey is null) return;
        if (_capturePage.ManualCaptureState == CaptureRunState.Capturing)
        {
            ManualCaptureKeyPill.Background = (Brush)FindResource("AccentBrush");
            ManualCaptureKeyPillText.Text = "F5 CAPTURING";
            return;
        }
        if (!_capturePage.IsManualSessionActive)
        {
            ManualCaptureKeyPill.Background = (Brush)FindResource("MutedBrush");
            ManualCaptureKeyPillText.Text = "F5 IDLE";
            return;
        }
        ManualCaptureKeyPill.Background = (Brush)FindResource(
            _capturePage.ManualCaptureState == CaptureRunState.Failed ? "DangerBrush" : "SuccessBrush");
        ManualCaptureKeyPillText.Text = _capturePage.ManualCaptureState == CaptureRunState.Failed
            ? "F5 FAILED"
            : "F5 READY";
    }

    private async Task NavigateAsync(PageKind target)
    {
        if (_currentPage == PageKind.Review && _pages[PageKind.Review] is ReviewPage review)
        {
            await review.FlushPendingAsync();
        }

        _currentPage = target;
        PageHost.Content = _pages[target];
        SetActiveNavigation(target);
        await _pages[target].RefreshAsync();
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
            RobloxPill.Background = (Brush)FindResource("MutedBrush");
            RobloxPillText.Text = "ROBLOX: OFFLINE";
        }
        else if (_workspace.WindowIsReady)
        {
            RobloxPill.Background = (Brush)FindResource("SuccessBrush");
            RobloxPillText.Text = $"ROBLOX: {_workspace.TargetSize}";
        }
        else
        {
            RobloxPill.Background = (Brush)FindResource("YellowBrush");
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
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_closingAfterFlush) return;
        eventArgs.Cancel = true;
        try
        {
            if (_pages[PageKind.Review] is ReviewPage review) await review.FlushPendingAsync();
        }
        finally
        {
            _closingAfterFlush = true;
            if (_windowSource is not null) _windowSource.RemoveHook(WindowMessageHook);
            _manualCaptureHotkey?.Dispose();
            _timedCaptureHotkey?.Dispose();
            _ocr.Dispose();
            _workspace.Dispose();
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

    private void Minimize_OnClick(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void Maximize_OnClick(object sender, RoutedEventArgs eventArgs) => ToggleMaximize();

    private void Close_OnClick(object sender, RoutedEventArgs eventArgs) => Close();
}
