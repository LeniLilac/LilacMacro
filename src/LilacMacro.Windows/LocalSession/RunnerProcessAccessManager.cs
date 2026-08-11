using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LilacMacro.Windows.LocalSession;

internal static class RunnerProcessAccessManager
{
    internal const uint OwnerProcessQueryAccess = 0x1000;
    internal const uint OwnerTokenQueryAccess = 0x0008;
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint GrantAccess = 1;
    private const uint TrusteeIsSid = 0;
    private const uint TrusteeIsUser = 1;
    private const int SeKernelObject = 6;

    public static void GrantOwnerValidationAccess(string ownerSid)
    {
        SecurityIdentifier sid = new(ownerSid);
        byte[] sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        nint sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
        try
        {
            Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);
            GrantAccessToObject(GetCurrentProcess(), sidPointer, OwnerProcessQueryAccess, "process");
            if (!OpenProcessToken(
                GetCurrentProcess(),
                OwnerTokenQueryAccess | ReadControl | WriteDac,
                out nint token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The runner token ACL could not be opened.");
            }
            try { GrantAccessToObject(token, sidPointer, OwnerTokenQueryAccess, "token"); }
            finally { CloseHandle(token); }
        }
        finally
        {
            Marshal.FreeHGlobal(sidPointer);
        }
    }

    private static void GrantAccessToObject(nint handle, nint sid, uint permission, string objectName)
    {
        nint securityDescriptor = nint.Zero;
        nint replacementAcl = nint.Zero;
        try
        {
            uint result = GetSecurityInfo(
                handle,
                SeKernelObject,
                DaclSecurityInformation,
                out _,
                out _,
                out nint currentAcl,
                out _,
                out securityDescriptor);
            ThrowIfFailed(result, $"The runner {objectName} security descriptor could not be read.");
            ExplicitAccess access = new()
            {
                AccessPermissions = permission,
                AccessMode = GrantAccess,
                Trustee = new Trustee
                {
                    TrusteeForm = TrusteeIsSid,
                    TrusteeType = TrusteeIsUser,
                    Name = sid,
                },
            };
            result = SetEntriesInAcl(1, ref access, currentAcl, out replacementAcl);
            ThrowIfFailed(result, $"The runner {objectName} query ACL could not be created.");
            result = SetSecurityInfo(
                handle,
                SeKernelObject,
                DaclSecurityInformation,
                nint.Zero,
                nint.Zero,
                replacementAcl,
                nint.Zero);
            ThrowIfFailed(result, $"The runner {objectName} query ACL could not be applied.");
        }
        finally
        {
            if (replacementAcl != nint.Zero) LocalFree(replacementAcl);
            if (securityDescriptor != nint.Zero) LocalFree(securityDescriptor);
        }
    }

    private static void ThrowIfFailed(uint result, string action)
    {
        if (result != 0) throw new Win32Exception(checked((int)result), action);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExplicitAccess
    {
        public uint AccessPermissions;
        public uint AccessMode;
        public uint Inheritance;
        public Trustee Trustee;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Trustee
    {
        public nint MultipleTrustee;
        public uint MultipleTrusteeOperation;
        public uint TrusteeForm;
        public uint TrusteeType;
        public nint Name;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint desiredAccess, out nint token);
    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(nint handle, int objectType, uint securityInfo, out nint owner, out nint group, out nint dacl, out nint sacl, out nint securityDescriptor);
    [DllImport("advapi32.dll", EntryPoint = "SetEntriesInAclW")]
    private static extern uint SetEntriesInAcl(uint count, ref ExplicitAccess entries, nint oldAcl, out nint newAcl);
    [DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(nint handle, int objectType, uint securityInfo, nint owner, nint group, nint dacl, nint sacl);
    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
