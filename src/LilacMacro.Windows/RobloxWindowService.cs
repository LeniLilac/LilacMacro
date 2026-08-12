using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LilacMacro.Core.Geometry;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

public sealed class RobloxWindowService
{
    private const int MaximumResizeAttempts = 6;
    private readonly SemaphoreSlim _resizeGate = new(1, 1);

    public IReadOnlyList<RobloxWindow> FindAll()
    {
        List<RobloxWindow> matches = [];
        NativeMethods.EnumWindowsProc callback = (window, _) =>
        {
            if (NativeMethods.IsWindowVisible(window) && TryDescribe(window, out RobloxWindow match))
            {
                matches.Add(match);
            }
            return true;
        };
        if (!NativeMethods.EnumWindows(callback, nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enumerate Roblox windows.");
        }

        nint foreground = NativeMethods.GetForegroundWindow();
        return matches
            .OrderByDescending(window => window.Handle == foreground)
            .ThenBy(window => ProcessPreference(window.ProcessName))
            .ThenByDescending(window => TryGetArea(window.Handle))
            .ToArray();
    }

    public RobloxWindow? FindBest() => FindAll().FirstOrDefault() is { Handle: not 0 } window ? window : null;

    public ClientBounds GetClientBounds(RobloxWindow window)
    {
        nint handle = Revalidate(window);
        if (!NativeMethods.GetClientRect(handle, out NativeMethods.Rect rectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the Roblox client rectangle.");
        }

        NativeMethods.Point topLeft = new() { X = rectangle.Left, Y = rectangle.Top };
        NativeMethods.Point bottomRight = new() { X = rectangle.Right, Y = rectangle.Bottom };
        if (!NativeMethods.ClientToScreen(handle, ref topLeft) ||
            !NativeMethods.ClientToScreen(handle, ref bottomRight))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not locate the Roblox client area.");
        }

        int width = bottomRight.X - topLeft.X;
        int height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The Roblox client has no capturable area.");
        }
        return new ClientBounds(topLeft.X, topLeft.Y, width, height);
    }

    public async Task<ResizeResult> ResizeClientAsync(
        RobloxWindow window,
        PixelSize target,
        CancellationToken cancellationToken = default)
    {
        target = PixelSize.Create(target.Width, target.Height);
        await _resizeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            nint handle = Revalidate(window);
            PixelSize previous = GetClientBounds(window).Size;
            if (await HasStableSizeAsync(window, target, cancellationToken).ConfigureAwait(false))
            {
                return new ResizeResult(previous, target, 0, elapsed.Elapsed);
            }

            if (NativeMethods.IsIconic(handle) || NativeMethods.IsZoomed(handle))
            {
                NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            }

            for (int attempt = 1; attempt <= MaximumResizeAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ClientBounds client = GetClientBounds(window);
                WindowBounds outer = GetWindowBounds(handle);
                int requestedOuterWidth = checked(outer.Width + target.Width - client.Width);
                int requestedOuterHeight = checked(outer.Height + target.Height - client.Height);
                (int x, int y) = FitToMonitor(handle, outer.X, outer.Y, requestedOuterWidth, requestedOuterHeight);

                if (!NativeMethods.SetWindowPos(
                        handle,
                        nint.Zero,
                        x,
                        y,
                        requestedOuterWidth,
                        requestedOuterHeight,
                        NativeMethods.SwpNoZOrder |
                        NativeMethods.SwpNoActivate |
                        NativeMethods.SwpFrameChanged |
                        NativeMethods.SwpShowWindow))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not resize Roblox.");
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                if (await HasStableSizeAsync(window, target, cancellationToken).ConfigureAwait(false))
                {
                    return new ResizeResult(previous, target, attempt, elapsed.Elapsed);
                }
            }

            PixelSize actual = GetClientBounds(window).Size;
            throw new InvalidOperationException(
                $"Roblox did not accept the requested {target} client size after {MaximumResizeAttempts} verified attempts. Actual size: {actual}.");
        }
        finally
        {
            _resizeGate.Release();
        }
    }

    public async Task<ClientBounds> EnsureClientVisibleAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        CancellationToken cancellationToken = default)
    {
        await _resizeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            nint handle = Revalidate(window);
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ClientBounds client = GetClientBounds(window);
                if (client.Size != expectedSize)
                    throw new InvalidOperationException($"Roblox is {client.Size}; input requires {expectedSize}.");

                ScreenWorkArea workArea = GetMonitorWorkArea(handle);
                WindowBounds outer = GetWindowBounds(handle);
                WindowBounds fitted = RobloxClientVisibilityPolicy.FitWindow(client, outer, workArea);
                if (fitted.X == outer.X && fitted.Y == outer.Y) return client;

                if (!NativeMethods.SetWindowPos(
                        handle,
                        nint.Zero,
                        fitted.X,
                        fitted.Y,
                        fitted.Width,
                        fitted.Height,
                        NativeMethods.SwpNoZOrder |
                        NativeMethods.SwpNoActivate |
                        NativeMethods.SwpShowWindow))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not bring Roblox fully into view.");
                }

                await Task.Delay(75, cancellationToken).ConfigureAwait(false);
                ClientBounds observed = GetClientBounds(window);
                if (observed.Size == expectedSize &&
                    RobloxClientVisibilityPolicy.IsFullyVisible(observed, GetMonitorWorkArea(handle)))
                {
                    return observed;
                }
            }
            throw new InvalidOperationException("Roblox did not remain fully inside the usable monitor area.");
        }
        finally
        {
            _resizeGate.Release();
        }
    }

    internal WindowBounds GetWindowBounds(RobloxWindow window) => GetWindowBounds(Revalidate(window));

    internal WindowBounds? GetExtendedFrameBounds(RobloxWindow window)
    {
        nint handle = Revalidate(window);
        uint size = (uint)Marshal.SizeOf<NativeMethods.Rect>();
        int result = NativeMethods.DwmGetWindowAttribute(
            handle,
            NativeMethods.DwmwaExtendedFrameBounds,
            out NativeMethods.Rect rectangle,
            size);
        return result == 0 && rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top
            ? new WindowBounds(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top)
            : null;
    }

    internal nint Revalidate(RobloxWindow window)
    {
        if (!NativeMethods.IsWindow(window.Handle) ||
            !TryDescribe(window.Handle, out RobloxWindow current) ||
            current.ProcessId != window.ProcessId)
        {
            throw new InvalidOperationException("The selected Roblox window is no longer available.");
        }
        return window.Handle;
    }

    private async Task<bool> HasStableSizeAsync(
        RobloxWindow window,
        PixelSize target,
        CancellationToken cancellationToken)
    {
        if (GetClientBounds(window).Size != target) return false;
        await Task.Delay(80, cancellationToken).ConfigureAwait(false);
        return GetClientBounds(window).Size == target;
    }

    private static WindowBounds GetWindowBounds(nint handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out NativeMethods.Rect rectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the Roblox window bounds.");
        }
        return new WindowBounds(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
    }

    private static (int X, int Y) FitToMonitor(nint handle, int x, int y, int width, int height)
    {
        nint monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info)) return (x, y);

        int fittedX = width >= info.Work.Right - info.Work.Left
            ? info.Work.Left
            : Math.Clamp(x, info.Work.Left, info.Work.Right - width);
        int fittedY = height >= info.Work.Bottom - info.Work.Top
            ? info.Work.Top
            : Math.Clamp(y, info.Work.Top, info.Work.Bottom - height);
        return (fittedX, fittedY);
    }

    private static ScreenWorkArea GetMonitorWorkArea(nint handle)
    {
        nint monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the Roblox monitor work area.");
        return new ScreenWorkArea(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    private static bool TryDescribe(nint handle, out RobloxWindow window)
    {
        window = default;
        if (NativeMethods.GetWindowThreadProcessId(handle, out uint processId) == 0 || processId == 0) return false;
        try
        {
            using Process process = Process.GetProcessById(checked((int)processId));
            string processName = process.ProcessName;
            if (!IsSupportedProcess(processName)) return false;
            string title = ReadTitle(handle);
            window = new RobloxWindow(handle, string.IsNullOrWhiteSpace(title) ? "Roblox" : title, checked((int)processId), processName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool IsSupportedProcess(string name) =>
        name.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Windows10Universal", StringComparison.OrdinalIgnoreCase);

    private static int ProcessPreference(string name) =>
        name.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static string ReadTitle(nint handle)
    {
        int length = NativeMethods.GetWindowTextLength(handle);
        StringBuilder title = new(Math.Max(1, length + 1));
        _ = NativeMethods.GetWindowText(handle, title, title.Capacity);
        return title.ToString();
    }

    private static long TryGetArea(nint handle)
    {
        if (!NativeMethods.GetClientRect(handle, out NativeMethods.Rect rectangle)) return 0;
        return Math.Max(0L, (long)(rectangle.Right - rectangle.Left) * (rectangle.Bottom - rectangle.Top));
    }
}
