using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LilacMacro.Windows.Interop;

internal static class NativeWindowProperties
{
    internal static nint Read(nint window, int index)
    {
        Marshal.SetLastPInvokeError(0);
        nint value = NativeMethods.GetWindowLongPtr(window, index);
        int error = Marshal.GetLastPInvokeError();
        return value != nint.Zero || error == 0
            ? value
            : throw new Win32Exception(error);
    }

    internal static bool TryRead(nint window, int index, out nint value)
    {
        try
        {
            value = Read(window, index);
            return true;
        }
        catch (Win32Exception)
        {
            value = nint.Zero;
            return false;
        }
    }

    internal static void Write(nint window, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = NativeMethods.SetWindowLongPtr(window, index, value);
        int error = Marshal.GetLastPInvokeError();
        if (previous == nint.Zero && error != 0) throw new Win32Exception(error);
    }
}
