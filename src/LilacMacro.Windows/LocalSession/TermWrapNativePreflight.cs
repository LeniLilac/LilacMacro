using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LilacMacro.Windows.LocalSession;

internal sealed record TermWrapNativePreflightResult(
    bool Started,
    bool TargetModuleLoaded,
    bool TimedOut,
    int? ExitCode,
    IReadOnlyList<string> Diagnostics);

internal sealed class TermWrapNativePreflight
{
    internal const string ProbeExportName = "ServiceMain";
    private const uint DebugOnlyThisProcess = 0x00000002;
    private const uint CreateNoWindow = 0x08000000;
    private const uint DbgContinue = 0x00010002;
    private const uint DbgExceptionNotHandled = 0x80010001;
    private const uint ExceptionDebugEvent = 1;
    private const uint CreateProcessDebugEvent = 3;
    private const uint ExitProcessDebugEvent = 5;
    private const uint LoadDllDebugEvent = 6;
    private const uint OutputDebugStringEvent = 8;
    private const uint ExceptionBreakpoint = 0x80000003;

    public Task<TermWrapNativePreflightResult> RunAsync(
        string termWrapPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => Task.Factory.StartNew(
            () => Run(termWrapPath, timeout, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);

    private static TermWrapNativePreflightResult Run(
        string termWrapPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string dllPath = Path.GetFullPath(termWrapPath);
        string rundll32 = Path.Combine(Environment.SystemDirectory, "rundll32.exe");
        if (!File.Exists(dllPath)) throw new FileNotFoundException("Pinned TermWrap binary is missing.", dllPath);
        if (!File.Exists(rundll32)) throw new FileNotFoundException("Windows rundll32.exe is missing.", rundll32);

        StartupInfo startup = new() { Size = Marshal.SizeOf<StartupInfo>() };
        StringBuilder command = new($"\"{rundll32}\" \"{dllPath}\",{ProbeExportName}");
        if (!CreateProcess(
                rundll32,
                command,
                nint.Zero,
                nint.Zero,
                false,
                DebugOnlyThisProcess | CreateNoWindow,
                nint.Zero,
                Path.GetDirectoryName(dllPath),
                ref startup,
                out ProcessInformation process))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the isolated TermWrap compatibility probe.");

        List<string> diagnostics = [];
        bool loaded = false;
        bool timedOut = false;
        int? exitCode = null;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        try
        {
            while (exitCode is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    timedOut = true;
                    TerminateProcess(process.Process, 0xDEAD);
                    break;
                }
                if (!WaitForDebugEvent(out DebugEvent debugEvent, 100))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 121)
                    {
                        string detail = new Win32Exception(error).Message;
                        throw new Win32Exception(
                            error,
                            $"The compatibility probe debugger failed ({error}: {detail}).");
                    }
                    continue;
                }

                uint continuation = debugEvent.Code == ExceptionDebugEvent
                    && debugEvent.Data.Exception.Record.Code != ExceptionBreakpoint
                        ? DbgExceptionNotHandled
                        : DbgContinue;
                try
                {
                    switch (debugEvent.Code)
                    {
                        case CreateProcessDebugEvent:
                            CloseIfPresent(debugEvent.Data.CreateProcess.File);
                            if (debugEvent.Data.CreateProcess.Thread != process.Thread)
                                CloseIfPresent(debugEvent.Data.CreateProcess.Thread);
                            if (debugEvent.Data.CreateProcess.Process != process.Process)
                                CloseIfPresent(debugEvent.Data.CreateProcess.Process);
                            break;
                        case LoadDllDebugEvent:
                            loaded |= IsTargetModule(debugEvent.Data.LoadDll.File, dllPath);
                            CloseIfPresent(debugEvent.Data.LoadDll.File);
                            break;
                        case OutputDebugStringEvent:
                            string? message = ReadDebugString(process.Process, debugEvent.Data.DebugString);
                            if (!string.IsNullOrWhiteSpace(message)) diagnostics.Add(message);
                            break;
                        case ExitProcessDebugEvent:
                            exitCode = unchecked((int)debugEvent.Data.ExitProcess.ExitCode);
                            break;
                    }
                }
                finally
                {
                    ContinueDebugEvent(debugEvent.ProcessId, debugEvent.ThreadId, continuation);
                }
            }
        }
        finally
        {
            if (timedOut) WaitForSingleObject(process.Process, 2000);
            CloseHandle(process.Thread);
            CloseHandle(process.Process);
        }
        return new(true, loaded, timedOut, exitCode, diagnostics);
    }

    private static string? ReadDebugString(nint process, OutputDebugStringInfo info)
    {
        int characterCount = Math.Min(info.Length, (ushort)8192);
        if (characterCount == 0 || info.Data == nint.Zero) return null;
        int byteCount = info.Unicode != 0 ? characterCount * 2 : characterCount;
        byte[] bytes = new byte[byteCount];
        if (!ReadProcessMemory(process, info.Data, bytes, bytes.Length, out nuint read) || read == 0) return null;
        return (info.Unicode != 0 ? Encoding.Unicode : Encoding.Default)
            .GetString(bytes, 0, checked((int)read)).TrimEnd('\0', '\r', '\n');
    }

    private static bool IsTargetModule(nint file, string expectedPath)
    {
        if (file == nint.Zero) return false;
        StringBuilder path = new(1024);
        uint length = GetFinalPathNameByHandle(file, path, (uint)path.Capacity, 0);
        if (length == 0 || length >= path.Capacity) return false;
        string actual = path.ToString();
        if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal)) actual = actual[4..];
        return string.Equals(Path.GetFullPath(actual), expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void CloseIfPresent(nint handle)
    {
        if (handle != nint.Zero && handle != new nint(-1)) CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public nint Reserved2Pointer;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process; public nint Thread; public uint ProcessId; public uint ThreadId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DebugEvent { public uint Code; public uint ProcessId; public uint ThreadId; public DebugEventData Data; }

    [StructLayout(LayoutKind.Explicit, Size = 160)]
    private struct DebugEventData
    {
        [FieldOffset(0)] public ExceptionDebugInfo Exception;
        [FieldOffset(0)] public CreateProcessDebugInfo CreateProcess;
        [FieldOffset(0)] public ExitProcessDebugInfo ExitProcess;
        [FieldOffset(0)] public LoadDllDebugInfo LoadDll;
        [FieldOffset(0)] public OutputDebugStringInfo DebugString;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ExceptionDebugInfo { public ExceptionRecord Record; public uint FirstChance; }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ExceptionRecord
    {
        public uint Code;
        public uint Flags;
        public nint NestedRecord;
        public nint Address;
        public uint ParameterCount;
        public fixed ulong Information[15];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateProcessDebugInfo
    {
        public nint File; public nint Process; public nint Thread; public nint BaseOfImage;
        public uint DebugInfoOffset; public uint DebugInfoSize; public nint ThreadLocalBase;
        public nint StartAddress; public nint ImageName; public ushort Unicode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExitProcessDebugInfo { public uint ExitCode; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LoadDllDebugInfo
    {
        public nint File; public nint BaseOfDll; public uint DebugInfoOffset; public uint DebugInfoSize;
        public nint ImageName; public ushort Unicode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputDebugStringInfo { public nint Data; public ushort Unicode; public ushort Length; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder commandLine, nint processAttributes,
        nint threadAttributes, bool inheritHandles, uint creationFlags, nint environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitForDebugEvent(out DebugEvent debugEvent, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ContinueDebugEvent(uint processId, uint threadId, uint continueStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, int size, out nuint read);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(nint file, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
}
