using System.ComponentModel;
using System.Runtime.InteropServices;
using LilacMacro.Core.LocalSession;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

internal sealed class RunnerElevatedProfilePolicyApplier
{
    private const int Logon32LogonInteractive = 2;
    private const int Logon32ProviderDefault = 0;
    private const int ProfileNoUi = 1;

    public IReadOnlyList<string> Apply(
        string accountName,
        string password,
        string runnerSid,
        IReadOnlyList<RunnerRegistryRule> rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerSid);
        if (!rules.SequenceEqual(RunnerProfilePolicy.DefaultElevatedRegistryRules))
            throw new InvalidDataException("Runner elevated registry policy is outside the exact allowlist.");

        using RegistryKey? existing = Registry.Users.OpenSubKey(runnerSid, writable: true);
        if (existing is not null) return ApplyRules(existing, rules);

        if (!LogonUser(
                accountName,
                ".",
                password,
                Logon32LogonInteractive,
                Logon32ProviderDefault,
                out nint token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not open the runner profile token.");
        }

        try
        {
            ProfileInfo profile = new()
            {
                Size = Marshal.SizeOf<ProfileInfo>(),
                Flags = ProfileNoUi,
                UserName = accountName,
            };
            bool loadedBySetup = LoadUserProfile(token, ref profile);
            int loadError = loadedBySetup ? 0 : Marshal.GetLastWin32Error();
            try
            {
                IReadOnlyList<string> applied;
                using (RegistryKey? root = Registry.Users.OpenSubKey(runnerSid, writable: true))
                {
                    if (root is null)
                        throw new Win32Exception(loadError, "Windows could not load the runner registry hive.");
                    applied = ApplyRules(root, rules);
                }
                if (loadedBySetup && !UnloadUserProfile(token, profile.Profile))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not unload the runner registry hive.");
                loadedBySetup = false;
                return applied;
            }
            catch
            {
                if (loadedBySetup) _ = UnloadUserProfile(token, profile.Profile);
                throw;
            }
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    private static IReadOnlyList<string> ApplyRules(
        RegistryKey root,
        IEnumerable<RunnerRegistryRule> rules)
    {
        List<string> applied = [];
        foreach (RunnerRegistryRule rule in rules)
            RunnerProfilePolicyApplier.ApplyRegistryRule(root, rule, applied);
        return applied;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProfileInfo
    {
        public int Size;
        public int Flags;
        public string? UserName;
        public string? ProfilePath;
        public string? DefaultPath;
        public string? ServerName;
        public string? PolicyPath;
        public nint Profile;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(
        string userName,
        string domain,
        string password,
        int logonType,
        int logonProvider,
        out nint token);

    [DllImport("userenv.dll", EntryPoint = "LoadUserProfileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LoadUserProfile(nint token, ref ProfileInfo profileInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnloadUserProfile(nint token, nint profile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
