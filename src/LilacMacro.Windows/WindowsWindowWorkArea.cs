using System.Runtime.InteropServices;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

public static class WindowsWindowWorkArea
{
    public static bool TryGet(
        nint window,
        double dpiScaleX,
        double dpiScaleY,
        out DesktopWorkAreaBounds workArea)
    {
        workArea = default;
        if (window == nint.Zero || dpiScaleX <= 0 || dpiScaleY <= 0) return false;
        nint monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero) return false;
        NativeMethods.MonitorInfo monitorInfo = new()
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) return false;
        NativeMethods.Rect work = monitorInfo.Work;
        workArea = new DesktopWorkAreaBounds(
            work.Left / dpiScaleX,
            work.Top / dpiScaleY,
            (work.Right - work.Left) / dpiScaleX,
            (work.Bottom - work.Top) / dpiScaleY);
        return workArea.Width > 0 && workArea.Height > 0;
    }

    public static DesktopWorkAreaBounds FitNormalBounds(
        DesktopWorkAreaBounds current,
        DesktopWorkAreaBounds workArea,
        double desiredWidth,
        double desiredHeight)
    {
        double width = Math.Min(Math.Max(current.Width, desiredWidth), workArea.Width);
        double height = Math.Min(Math.Max(current.Height, desiredHeight), workArea.Height);
        double centeredLeft = current.Left + current.Width / 2 - width / 2;
        double centeredTop = current.Top + current.Height / 2 - height / 2;
        double maximumLeft = Math.Max(workArea.Left, workArea.Right - width);
        double maximumTop = Math.Max(workArea.Top, workArea.Bottom - height);
        return new DesktopWorkAreaBounds(
            Math.Clamp(centeredLeft, workArea.Left, maximumLeft),
            Math.Clamp(centeredTop, workArea.Top, maximumTop),
            width,
            height);
    }
}

public readonly record struct DesktopWorkAreaBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}
