using System.ComponentModel;
using System.Runtime.InteropServices;
using LilacMacro.Core.Geometry;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

public sealed class RobloxWindowDockService(RobloxWindowService windows) : IDisposable
{
    public const int ClientWidth = 1366;
    public const int ClientHeight = 700;

    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsSystemMenu = 0x00080000L;
    private const long WsPopup = 0x80000000L;
    private const long WsExTopmost = 0x00000008L;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExAppWindow = 0x00040000L;
    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint HwndNotTopmost = new(-2);

    private readonly object _gate = new();
    private DockedWindowState? _state;

    public bool IsDocked
    {
        get
        {
            lock (_gate) return TryIsDocked(_state);
        }
    }

    public bool HasTrackedSource
    {
        get
        {
            lock (_gate)
            {
                ForgetClosedSourceCore();
                return _state is not null;
            }
        }
    }

    public nint SourceHandle
    {
        get
        {
            lock (_gate)
            {
                ForgetClosedSourceCore();
                return _state?.Source.Handle ?? nint.Zero;
            }
        }
    }

    public bool IsSourceForeground
    {
        get
        {
            lock (_gate)
            {
                return _state is { } state &&
                    NativeMethods.GetForegroundWindow() == state.Source.Handle;
            }
        }
    }

    public bool IsForeground(RobloxWindow source)
    {
        try
        {
            return windows.Revalidate(source) == NativeMethods.GetForegroundWindow();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool IsDashboardExposed(nint owner, nint knownSource)
    {
        lock (_gate)
        {
            nint source = _state is { } state && NativeMethods.IsWindow(state.Source.Handle)
                ? state.Source.Handle
                : knownSource;
            return RobloxDockExposure.IsExposed(owner, source);
        }
    }

    public void Dock(RobloxWindow source, int screenX, int screenY)
    {
        lock (_gate)
        {
            WindowBounds screenBounds = new(screenX, screenY, ClientWidth, ClientHeight);
            if (_state is { } current && current.Source == source)
            {
                MaintainDockCore(current, screenBounds);
                return;
            }
            if (_state is not null && !TryUndockCore(out string error))
            {
                throw new InvalidOperationException(error);
            }

            nint handle = windows.Revalidate(source);
            nint originalStyle = NativeWindowProperties.Read(handle, NativeMethods.GwlStyle);
            if ((originalStyle.ToInt64() & NativeMethods.WsChild) != 0)
            {
                throw new InvalidOperationException(
                    "Roblox is embedded by another application. Close that application, restart Roblox, and try again.");
            }

            DockedWindowState state = new(
                source,
                originalStyle,
                NativeWindowProperties.Read(handle, NativeMethods.GwlExStyle),
                ReadBounds(handle));
            try
            {
                MaintainDockCore(state, screenBounds, forceFrameChanged: true);
                _state = state;
            }
            catch
            {
                TryRestoreAfterDockFailure(state);
                throw;
            }
        }
    }

    public void UpdateBounds(int screenX, int screenY)
    {
        lock (_gate)
        {
            if (_state is not { } state || !IsSourceAvailable(state)) return;
            WindowBounds screenBounds = new(screenX, screenY, ClientWidth, ClientHeight);
            MaintainDockCore(state, screenBounds);
        }
    }

    public bool TryUndock(out string error)
    {
        lock (_gate) return TryUndockCore(out error);
    }

    public bool TrySuspend(out string error)
    {
        lock (_gate) return TryReleaseCore(showRestoredWindow: false, out error);
    }

    public bool TryReleaseAndMinimize(out string error)
    {
        lock (_gate)
        {
            if (_state is not { } state)
            {
                error = string.Empty;
                return true;
            }
            if (!IsSourceAvailable(state))
            {
                _state = null;
                error = string.Empty;
                return true;
            }

            try
            {
                Restore(state, showRestoredWindow: false);
                _ = NativeMethods.ShowWindowAsync(state.Source.Handle, NativeMethods.SwShowMinNoActive);
                _state = null;
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                error = $"Windows could not minimize Roblox after releasing the dock: {exception.Message}";
                return false;
            }
        }
    }

    public void Dispose() => _ = TryUndock(out _);

    internal static long BuildDockedStyle(long originalStyle)
    {
        long normalized = unchecked((uint)originalStyle);
        long removed = WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox |
            WsSystemMenu | NativeMethods.WsChild;
        return (normalized & ~removed) | WsPopup | NativeMethods.WsVisible;
    }

    internal static long BuildDockedExtendedStyle(long originalStyle)
    {
        long normalized = unchecked((uint)originalStyle);
        return (normalized & ~(WsExNoActivate | WsExAppWindow)) | WsExTopmost;
    }

    private bool TryUndockCore(out string error) => TryReleaseCore(showRestoredWindow: true, out error);

    private bool TryReleaseCore(bool showRestoredWindow, out string error)
    {
        if (_state is not { } state)
        {
            error = string.Empty;
            return true;
        }
        if (!IsSourceAvailable(state))
        {
            _state = null;
            error = string.Empty;
            return true;
        }

        try
        {
            Restore(state, showRestoredWindow);
            _state = null;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            error = $"Windows could not return Roblox to its standalone window: {exception.Message}";
            return false;
        }
    }

    private static void Restore(DockedWindowState state, bool showRestoredWindow)
    {
        NativeWindowProperties.Write(state.Source.Handle, NativeMethods.GwlStyle, state.OriginalStyle);
        NativeWindowProperties.Write(state.Source.Handle, NativeMethods.GwlExStyle, state.OriginalExtendedStyle);
        uint flags = NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged;
        if (showRestoredWindow) flags |= NativeMethods.SwpShowWindow;
        if (!NativeMethods.SetWindowPos(
                state.Source.Handle,
                HwndNotTopmost,
                state.OriginalBounds.X,
                state.OriginalBounds.Y,
                state.OriginalBounds.Width,
                state.OriginalBounds.Height,
                flags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not restore the Roblox window bounds.");
        }
        if (showRestoredWindow) _ = NativeMethods.ShowWindowAsync(state.Source.Handle, NativeMethods.SwRestore);
    }

    private static void TryRestoreAfterDockFailure(DockedWindowState state)
    {
        try
        {
            Restore(state, showRestoredWindow: true);
        }
        catch
        {
            // Preserve the original docking failure.
        }
    }

    private bool TryIsDocked(DockedWindowState? state)
    {
        if (state is null || !IsSourceAvailable(state) ||
            !NativeWindowProperties.TryRead(state.Source.Handle, NativeMethods.GwlStyle, out nint style) ||
            !NativeWindowProperties.TryRead(state.Source.Handle, NativeMethods.GwlExStyle, out nint extendedStyle))
        {
            return false;
        }
        return (style.ToInt64() & NativeMethods.WsChild) == 0 &&
            (extendedStyle.ToInt64() & WsExTopmost) != 0;
    }

    private void ForgetClosedSourceCore()
    {
        if (_state is { } state && !IsSourceAvailable(state)) _state = null;
    }

    private bool IsSourceAvailable(DockedWindowState state)
    {
        try
        {
            _ = windows.Revalidate(state.Source);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void MaintainDockCore(
        DockedWindowState state,
        WindowBounds bounds,
        bool forceFrameChanged = false)
    {
        nint handle = windows.Revalidate(state.Source);
        nint currentStyle = NativeWindowProperties.Read(handle, NativeMethods.GwlStyle);
        nint currentExtendedStyle = NativeWindowProperties.Read(handle, NativeMethods.GwlExStyle);
        long desiredStyle = BuildDockedStyle(state.OriginalStyle.ToInt64());
        long desiredExtendedStyle = BuildDockedExtendedStyle(state.OriginalExtendedStyle.ToInt64());
        bool styleChanged = unchecked((uint)currentStyle.ToInt64()) != unchecked((uint)desiredStyle);
        bool extendedStyleChanged = unchecked((uint)currentExtendedStyle.ToInt64()) !=
            unchecked((uint)desiredExtendedStyle);

        if (styleChanged)
        {
            NativeWindowProperties.Write(handle, NativeMethods.GwlStyle, new nint(desiredStyle));
        }
        if (extendedStyleChanged)
        {
            NativeWindowProperties.Write(handle, NativeMethods.GwlExStyle, new nint(desiredExtendedStyle));
        }

        UpdateBoundsCore(handle, bounds, forceFrameChanged || styleChanged || extendedStyleChanged);
        PixelSize actual = windows.GetClientBounds(state.Source).Size;
        if (actual != PixelSize.Create(ClientWidth, ClientHeight))
        {
            throw new InvalidOperationException(
                $"Roblox did not accept the required {ClientWidth} x {ClientHeight} client size. Actual: {actual}.");
        }
    }

    private static void UpdateBoundsCore(nint source, WindowBounds bounds, bool frameChanged = false)
    {
        uint flags = NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow;
        if (frameChanged) flags |= NativeMethods.SwpFrameChanged;
        if (!NativeMethods.SetWindowPos(
                source,
                HwndTopmost,
                bounds.X,
                bounds.Y,
                ClientWidth,
                ClientHeight,
                flags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not position docked Roblox.");
        }
        _ = NativeMethods.ShowWindowAsync(source, NativeMethods.SwShowNoActivate);
    }

    private static WindowBounds ReadBounds(nint window)
    {
        if (!NativeMethods.GetWindowRect(window, out NativeMethods.Rect bounds))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the Roblox window bounds.");
        }
        return new WindowBounds(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    private sealed record DockedWindowState(
        RobloxWindow Source,
        nint OriginalStyle,
        nint OriginalExtendedStyle,
        WindowBounds OriginalBounds);
}
