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
    private const int CaptureHotkeyId = 0x4C4D;
    private readonly WorkspaceController _workspace = new();
    private readonly OcrRunner _ocr = new();
    private readonly Dictionary<PageKind, IWorkspacePage> _pages;
    private readonly CapturePage _capturePage;
    private GlobalHotkeyRegistration? _captureHotkey;
    private HwndSource? _windowSource;
    private PageKind _currentPage = PageKind.Capture;
    private bool _closingAfterFlush;
    private bool _hotkeyCaptureStarting;

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
            RegisterCaptureHotkey();
            await NavigateAsync(PageKind.Capture);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "LilacMacro startup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegisterCaptureHotkey()
    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            _windowSource = HwndSource.FromHwnd(handle)
                ?? throw new InvalidOperationException("Could not attach the capture key to LilacMacro.");
            _windowSource.AddHook(WindowMessageHook);
            _captureHotkey = new GlobalHotkeyRegistration(
                handle,
                CaptureHotkeyId,
                GlobalHotkeyRegistration.F6VirtualKey);
            UpdateCaptureKeyStatus();
        }
        catch (Exception)
        {
            CaptureKeyPill.Background = (Brush)FindResource("DangerBrush");
            CaptureKeyPillText.Text = "F6 UNAVAILABLE";
        }
    }

    private nint WindowMessageHook(nint window, int message, nint parameter, nint data, ref bool handled)
    {
        if (_captureHotkey?.Matches(message, parameter) != true) return 0;
        handled = true;
        if (!_capturePage.IsCapturing && !_hotkeyCaptureStarting) _ = RunHotkeyCaptureAsync();
        return 0;
    }

    private async Task RunHotkeyCaptureAsync()
    {
        _hotkeyCaptureStarting = true;
        try
        {
            if (_currentPage == PageKind.Review && _pages[PageKind.Review] is ReviewPage review)
            {
                await review.FlushPendingAsync();
            }
            await _capturePage.CaptureFromHotkeyAsync();
        }
        finally
        {
            _hotkeyCaptureStarting = false;
            UpdateCaptureKeyStatus();
        }
    }

    private void CapturePage_OnCaptureStateChanged(object? sender, EventArgs eventArgs) => UpdateCaptureKeyStatus();

    private void UpdateCaptureKeyStatus()
    {
        if (_captureHotkey is null) return;
        CaptureKeyPill.Background = (Brush)FindResource(_capturePage.CaptureState switch
        {
            CaptureRunState.Capturing => "AccentBrush",
            CaptureRunState.Complete => "SuccessBrush",
            CaptureRunState.Failed => "DangerBrush",
            CaptureRunState.Cancelled => "YellowBrush",
            _ => "MutedBrush",
        });
        CaptureKeyPillText.Text = _capturePage.CaptureState switch
        {
            CaptureRunState.Capturing => "F6 CAPTURING",
            CaptureRunState.Complete => "F6 COMPLETE",
            CaptureRunState.Failed => "F6 FAILED",
            CaptureRunState.Cancelled => "F6 CANCELLED",
            _ => "F6 READY",
        };
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
            _captureHotkey?.Dispose();
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
