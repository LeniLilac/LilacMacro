using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LilacMacro.Windows.LocalSession;

public sealed class RunnerProcessLauncher
{
    private const uint LogonWithProfile = 1;
    private const uint CreateNoWindow = 0x08000000;

    public async Task<int> RunAndWaitAsync(
        string accountName,
        string password,
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string commandLine = string.Join(' ', new[] { Quote(executable) }.Concat(arguments.Select(Quote)));
        StartupInfo startup = new() { Size = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessWithLogon(accountName, ".", password, LogonWithProfile, null, commandLine, CreateNoWindow, nint.Zero, Path.GetDirectoryName(executable), ref startup, out ProcessInformation process))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The controlled runner logon could not start.");
        try
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            await WaitAsync(process.Process, deadline.Token).ConfigureAwait(false);
            if (!GetExitCodeProcess(process.Process, out uint exitCode)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return checked((int)exitCode);
        }
        finally
        {
            CloseHandle(process.Thread);
            CloseHandle(process.Process);
        }
    }

    private static async Task WaitAsync(nint handle, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint result = WaitForSingleObject(handle, 200);
            if (result == 0) return;
            if (result != 258) throw new Win32Exception(Marshal.GetLastWin32Error());
            await Task.Yield();
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo { public int Size; public string? Reserved, Desktop, Title; public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags; public short ShowWindow, Reserved2Count; public nint Reserved2, StandardInput, StandardOutput, StandardError; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process, Thread; public int ProcessId, ThreadId; }

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessWithLogonW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithLogon(string user, string domain, string password, uint logonFlags, string? application, string commandLine, uint creationFlags, nint environment, string? directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
