using System.Runtime.InteropServices;

namespace LilacMacro.Windows.LocalSession;

public static class RunnerSessionManager
{
    public static void LogoffAll(string accountName)
    {
        if (!WTSEnumerateSessions(nint.Zero, 0, 1, out nint sessions, out int count)) return;
        try
        {
            int size = Marshal.SizeOf<WtsSessionInfo>();
            for (int index = 0; index < count; index++)
            {
                WtsSessionInfo session = Marshal.PtrToStructure<WtsSessionInfo>(sessions + (index * size));
                string? user = QueryString(session.SessionId, 5);
                if (string.Equals(user, accountName, StringComparison.OrdinalIgnoreCase))
                    _ = WTSLogoffSession(nint.Zero, session.SessionId, true);
            }
        }
        finally { WTSFreeMemory(sessions); }
    }

    private static string? QueryString(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(nint.Zero, sessionId, infoClass, out nint value, out _)) return null;
        try { return Marshal.PtrToStringUni(value); }
        finally { WTSFreeMemory(value); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsSessionInfo { public int SessionId; public string WinStationName; public int State; }
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(nint server, int reserved, int version, out nint sessions, out int count);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(nint server, int sessionId, int infoClass, out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(nint server, int sessionId, bool wait);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint memory);
}
