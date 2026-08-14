using System.ComponentModel;
using System.Runtime.InteropServices;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

internal static class RobloxCursorMotion
{
    public static void SetAndPulse(int screenX, int screenY, int first, int second)
    {
        try
        {
            PositionWithRetry(screenX, screenY, "Windows could not position the pointer in Roblox.");
            NativeInputMethods.mouse_event(NativeInputMethods.MouseMove, first, 0, 0, 0);
            NativeInputMethods.mouse_event(NativeInputMethods.MouseMove, second, 0, 0, 0);
            PositionWithRetry(screenX, screenY, "Windows could not finish positioning the pointer.");
        }
        catch (Win32Exception error)
        {
            throw new RobloxPointerAcquisitionException(error.Message, error);
        }
    }

    public static void PositionWithRetry(int screenX, int screenY, string failure)
    {
        int nativeError = 0;
        for (int attempt = 1; attempt <= RobloxInputProtocol.CursorPositionAttemptCount; attempt++)
        {
            bool positioned = NativeInputMethods.SetCursorPos(screenX, screenY);
            if (!positioned) nativeError = Marshal.GetLastWin32Error();
            if (positioned && NativeInputMethods.GetCursorPos(out NativeMethods.Point observed) &&
                observed.X == screenX && observed.Y == screenY)
            {
                return;
            }
            if (attempt < RobloxInputProtocol.CursorPositionAttemptCount)
                Thread.Sleep(RobloxInputProtocol.CursorPositionRetryMilliseconds);
        }
        throw new Win32Exception(nativeError, failure);
    }
}
