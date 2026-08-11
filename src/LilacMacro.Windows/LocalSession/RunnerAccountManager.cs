using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LilacMacro.Windows.LocalSession;

public sealed class RunnerAccountManager
{
    private const uint UserPrivilegeUser = 1;
    private const uint UserFlagScript = 0x0001;
    private const uint UserFlagPasswordCannotChange = 0x0040;
    private const uint UserFlagNormalAccount = 0x0200;
    private const uint UserFlagPasswordNeverExpires = 0x10000;
    private const int ErrorUserExists = 2224;
    private const int ErrorMemberInAlias = 1378;
    private const int ErrorNoSuchUser = 2221;
    private const int SidTypeUser = 1;

    public string EnsureCreated(string accountName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        UserInfo1 user = new()
        {
            Name = accountName,
            Password = password,
            Privilege = UserPrivilegeUser,
            Flags = UserFlagScript | UserFlagPasswordCannotChange | UserFlagNormalAccount | UserFlagPasswordNeverExpires,
            Comment = "LilacMacro local runner account",
        };
        int result = NetUserAdd(null, 1, ref user, out _);
        if (result is not 0 and not ErrorUserExists)
            throw new Win32Exception(result, "Windows could not create the LilacMacro runner account.");

        string runnerSid = ResolveAccountSid(accountName);
        if (IsMemberOf("S-1-5-32-544", runnerSid))
            throw new InvalidOperationException("LilacMacroRunner is unexpectedly an administrator. Setup stopped without using it.");
        AddToGroup("S-1-5-32-555", runnerSid);
        if (!IsMemberOf("S-1-5-32-555", runnerSid))
            throw new InvalidOperationException("LilacMacroRunner is not a member of Remote Desktop Users after setup.");
        if (IsMemberOf("S-1-5-32-544", runnerSid))
            throw new InvalidOperationException("LilacMacroRunner gained administrator membership during setup.");
        return runnerSid;
    }

    public bool Exists(string accountName)
    {
        nint buffer = nint.Zero;
        try { return NetUserGetInfo(null, accountName, 0, out buffer) == 0; }
        finally { if (buffer != nint.Zero) NetApiBufferFree(buffer); }
    }

    public string? TryResolveSid(string accountName) => TryResolveAccountSid(accountName);

    public void SetPassword(string accountName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        UserInfo1003 user = new() { Password = password };
        int result = NetUserSetInfo(null, accountName, 1003, ref user, out _);
        if (result != 0)
            throw new Win32Exception(result, "Windows could not synchronize the runner account credential.");
    }

    public void Remove(string accountName, string? profilePath)
    {
        string? sid = TryResolveAccountSid(accountName);
        int result = NetUserDel(null, accountName);
        if (result is not 0 and not ErrorNoSuchUser)
            throw new Win32Exception(result, "Windows could not delete the LilacMacro runner account.");
        if (!string.IsNullOrWhiteSpace(profilePath) && Directory.Exists(profilePath))
        {
            if (sid is not null && !DeleteProfile(sid, profilePath, null))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not delete the runner profile.");
        }
    }

    private static void AddToGroup(string groupSid, string memberSid)
    {
        string groupName = ResolveSidName(groupSid);
        SecurityIdentifier sid = new(memberSid);
        byte[] sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        nint sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
        try
        {
            Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);
            LocalGroupMembersInfo0 member = new() { Sid = sidPointer };
            int result = NetLocalGroupAddMembers(null, groupName, 0, ref member, 1);
            if (result is not 0 and not ErrorMemberInAlias)
                throw new Win32Exception(result, "Windows could not add the runner to Remote Desktop Users.");
        }
        finally { Marshal.FreeHGlobal(sidPointer); }
    }

    private static bool IsMemberOf(string groupSid, string memberSid)
    {
        string groupName = ResolveSidName(groupSid);
        nint buffer = nint.Zero;
        try
        {
            int result = NetLocalGroupGetMembers(null, groupName, 0, out buffer, -1, out int read, out _, nint.Zero);
            if (result != 0) throw new Win32Exception(result, $"Windows could not inspect {groupName} membership.");
            int size = Marshal.SizeOf<LocalGroupMembersInfo0>();
            SecurityIdentifier expected = new(memberSid);
            for (int index = 0; index < read; index++)
            {
                LocalGroupMembersInfo0 item = Marshal.PtrToStructure<LocalGroupMembersInfo0>(buffer + (index * size));
                if (item.Sid != nint.Zero && new SecurityIdentifier(item.Sid).Equals(expected)) return true;
            }
            return false;
        }
        finally { if (buffer != nint.Zero) NetApiBufferFree(buffer); }
    }

    private static string ResolveAccountSid(string accountName) =>
        TryResolveAccountSid(accountName) ?? throw new InvalidOperationException("Windows created the runner but its SID could not be resolved.");

    private static string? TryResolveAccountSid(string accountName)
    {
        uint sidSize = 0;
        uint domainSize = 0;
        _ = LookupAccountName(null, accountName, null, ref sidSize, null, ref domainSize, out _);
        if (sidSize == 0) return null;
        byte[] sid = new byte[sidSize];
        char[] domain = new char[domainSize];
        return LookupAccountName(null, accountName, sid, ref sidSize, domain, ref domainSize, out int use) && use == SidTypeUser
            ? new SecurityIdentifier(sid, 0).Value
            : null;
    }

    private static string ResolveSidName(string value) =>
        ((NTAccount)new SecurityIdentifier(value).Translate(typeof(NTAccount))).Value.Split('\\').Last();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo1
    {
        public string Name;
        public string Password;
        public uint PasswordAge;
        public uint Privilege;
        public string? HomeDirectory;
        public string? Comment;
        public uint Flags;
        public string? ScriptPath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo1003 { public string Password; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupMembersInfo0 { public nint Sid; }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserAdd(string? server, int level, ref UserInfo1 user, out uint parameterError);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserDel(string? server, string user);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetInfo(string? server, string user, int level, out nint buffer);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserSetInfo(string? server, string userName, int level, ref UserInfo1003 user, out uint parameterError);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupAddMembers(string? server, string group, int level, ref LocalGroupMembersInfo0 member, int count);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(string? server, string group, int level, out nint buffer, int maximum, out int read, out int total, nint resume);
    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(nint buffer);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupAccountName(string? system, string account, byte[]? sid, ref uint sidSize, char[]? domain, ref uint domainSize, out int use);
    [DllImport("userenv.dll", EntryPoint = "DeleteProfileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteProfile(string sid, string? profilePath, string? computerName);
}
