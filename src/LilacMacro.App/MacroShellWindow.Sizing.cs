using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LilacMacro.Windows;

namespace LilacMacro.App;

public partial class MacroShellWindow
{
    private const double InitialWorkspaceWidth = 1920;
    private const double InitialWorkspaceHeight = 1080;
    private HwndSource? _windowSource;
    private HwndSourceHook? _windowHook;

    private void InitializeWindowSizing()
    {
        SourceInitialized += WindowSizing_OnSourceInitialized;
        ContentRendered += WindowSizing_OnContentRendered;
    }

    private void DisposeWindowSizing()
    {
        if (_windowSource is not null && _windowHook is not null) _windowSource.RemoveHook(_windowHook);
        _windowHook = null;
        _windowSource = null;
    }

    private void WindowSizing_OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowHook = WindowSizingMessage;
        _windowSource?.AddHook(_windowHook);
    }

    private void WindowSizing_OnContentRendered(object? sender, EventArgs eventArgs)
    {
        ContentRendered -= WindowSizing_OnContentRendered;
        if (WindowState != WindowState.Normal) return;
        nint handle = new WindowInteropHelper(this).Handle;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        if (!WindowsWindowWorkArea.TryGet(handle, dpi.DpiScaleX, dpi.DpiScaleY, out DesktopWorkAreaBounds workArea)) return;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        DesktopWorkAreaBounds fitted = WindowsWindowWorkArea.FitNormalBounds(
            new DesktopWorkAreaBounds(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height),
            workArea,
            InitialWorkspaceWidth,
            InitialWorkspaceHeight);
        Width = fitted.Width;
        Height = fitted.Height;
        Left = fitted.Left;
        Top = fitted.Top;
    }

    private nint WindowSizingMessage(
        nint window,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int minimumWidth = checked((int)Math.Ceiling(MinWidth * dpi.DpiScaleX));
        int minimumHeight = checked((int)Math.Ceiling(MinHeight * dpi.DpiScaleY));
        if (WindowsMaximizedWorkArea.TryApply(window, message, lParam, minimumWidth, minimumHeight)) handled = true;
        return nint.Zero;
    }
}
