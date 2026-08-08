using System.Runtime.InteropServices;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

public static class WindowsMaximizedWorkArea
{
    public static bool TryApply(
        nint window,
        int message,
        nint parameter,
        int minimumWidth,
        int minimumHeight)
    {
        if (message != NativeMethods.WmGetMinMaxInfo || window == nint.Zero || parameter == nint.Zero) return false;
        nint monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero) return false;
        NativeMethods.MonitorInfo monitorInfo = new()
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) return false;

        NativeMethods.MinMaxInfo sizing = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(parameter);
        ApplyBounds(ref sizing, monitorInfo.Monitor, monitorInfo.Work, minimumWidth, minimumHeight);
        Marshal.StructureToPtr(sizing, parameter, fDeleteOld: false);
        return true;
    }

    internal static void ApplyBounds(
        ref NativeMethods.MinMaxInfo sizing,
        NativeMethods.Rect monitor,
        NativeMethods.Rect workArea,
        int minimumWidth = 0,
        int minimumHeight = 0)
    {
        sizing.MaxPosition.X = workArea.Left - monitor.Left;
        sizing.MaxPosition.Y = workArea.Top - monitor.Top;
        sizing.MaxSize.X = workArea.Right - workArea.Left;
        sizing.MaxSize.Y = workArea.Bottom - workArea.Top;
        sizing.MinTrackSize.X = Math.Max(sizing.MinTrackSize.X, minimumWidth);
        sizing.MinTrackSize.Y = Math.Max(sizing.MinTrackSize.Y, minimumHeight);
    }
}
