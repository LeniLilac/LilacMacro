using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LilacMacro.Windows.LocalSession;

public static class SessionPipeClient
{
    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;

    public static async Task<NamedPipeClientStream> ConnectValidatedAsync(
        LocalSessionPaths paths,
        string expectedRunnerSid,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(
            ".",
            SessionPipe.Name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        try
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            ValidateServer(pipe, paths.WorkerPath, expectedRunnerSid);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    private static void ValidateServer(NamedPipeClientStream pipe, string expectedWorkerPath, string expectedRunnerSid)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId) || processId == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The runner pipe server identity is unavailable.");
        using Process process = Process.GetProcessById(checked((int)processId));
        string actualPath = process.MainModule?.FileName
            ?? throw new InvalidDataException("The runner process path is unavailable.");
        if (!string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedWorkerPath), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The local-session pipe is not owned by the installed worker executable.");
        if (!OpenProcessToken(process.Handle, TokenQuery, out nint token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The runner process token could not be inspected.");
        try
        {
            string sid = ReadTokenSid(token);
            if (!string.Equals(sid, expectedRunnerSid, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The local-session pipe server SID is not the provisioned runner SID.");
        }
        finally { CloseHandle(token); }
    }

    private static string ReadTokenSid(nint token)
    {
        _ = GetTokenInformation(token, TokenUserClass, nint.Zero, 0, out uint required);
        if (required == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Runner token size is unavailable.");
        nint buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!GetTokenInformation(token, TokenUserClass, buffer, required, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Runner token user is unavailable.");
            TokenUser user = Marshal.PtrToStructure<TokenUser>(buffer);
            return new SecurityIdentifier(user.User.Sid).Value;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes { public nint Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenUser { public SidAndAttributes User; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint access, out nint tokenHandle);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint tokenHandle, int informationClass, nint information, uint length, out uint returnLength);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
