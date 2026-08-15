using System.Runtime.InteropServices;
using LilacMacro.Core.Runtime;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

public static class WindowsRobloxDisplayScale
{
    internal const uint DefaultDpi = 96;

    public static void EnsureOneHundredPercent(RobloxWindow window) =>
        EnsureOneHundredPercent(window.Handle);

    internal static void EnsureOneHundredPercent(nint windowHandle)
    {
        (uint dpiX, uint dpiY) = ReadEffectiveDpi(windowHandle);
        if (dpiX == DefaultDpi && dpiY == DefaultDpi) return;
        throw new RobloxDisplayScaleException(
            ScalePercentageFromDpi(Math.Max(dpiX, dpiY)));
    }

    internal static int ScalePercentageFromDpi(uint dpi) =>
        checked((int)Math.Round(
            dpi * 100d / DefaultDpi,
            MidpointRounding.AwayFromZero));

    private static (uint X, uint Y) ReadEffectiveDpi(nint windowHandle)
    {
        nint monitor = NativeMethods.MonitorFromWindow(
            windowHandle,
            NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            throw new InvalidOperationException(
                "Windows could not identify the monitor containing Roblox.");
        }

        int result = NativeMethods.GetDpiForMonitor(
            monitor,
            NativeMethods.MonitorDpiTypeEffective,
            out uint dpiX,
            out uint dpiY);
        if (result < 0)
        {
            throw new COMException(
                "Windows could not read the display scale of the monitor containing Roblox.",
                result);
        }
        return (dpiX, dpiY);
    }
}
