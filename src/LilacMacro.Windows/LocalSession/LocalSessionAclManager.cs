using System.Security.AccessControl;
using System.Security.Principal;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public static class LocalSessionAclManager
{
    public static void SecureDirectory(string path, params string[] allowedSids)
    {
        Directory.CreateDirectory(path);
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (string sid in allowedSids.Distinct(StringComparer.Ordinal))
        {
            SecurityIdentifier identity = new(sid);
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        new DirectoryInfo(path).SetAccessControl(security);
    }

    public static void SecureSessionRoots(LocalSessionPaths paths, string ownerSid, string runnerSid)
    {
        string systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        string adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        SecureDirectory(paths.SessionRoot, ownerSid, systemSid, adminsSid);
        GrantReadOnly(paths.SessionRoot, runnerSid);
        SecureDirectory(paths.RunnerRoot, ownerSid, runnerSid, systemSid, adminsSid);
    }

    public static void SecureInstanceRoots(
        LocalSessionPaths paths,
        string ownerSid,
        IReadOnlyList<LocalRunnerProfile> profiles)
    {
        string systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        string adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        SecureDirectory(paths.SessionRoot, ownerSid, systemSid, adminsSid);
        foreach (LocalRunnerProfile profile in profiles) GrantReadOnly(paths.SessionRoot, profile.RunnerSid);
        SecureDirectory(paths.ProfilesRoot, ownerSid, systemSid, adminsSid);
        foreach (LocalRunnerProfile profile in profiles)
            SecureDirectory(paths.ProfileRoot(profile.Id), ownerSid, profile.RunnerSid, systemSid, adminsSid);
        string[] sharedSids = profiles
            .Where(profile => profile.ConfigurationMode == RunnerConfigurationMode.Shared)
            .Select(profile => profile.RunnerSid)
            .ToArray();
        SecureDirectory(paths.SharedConfigurationRoot, [ownerSid, systemSid, adminsSid, .. sharedSids]);
        foreach (LocalRunnerProfile profile in profiles.Where(profile => profile.ConfigurationMode == RunnerConfigurationMode.Isolated))
            SecureDirectory(paths.ConfigurationRootFor(profile), ownerSid, profile.RunnerSid, systemSid, adminsSid);
        SecureDirectory(
            paths.DiagnosticsRoot,
            [ownerSid, systemSid, adminsSid, .. profiles.Select(profile => profile.RunnerSid)]);
    }

    private static void GrantReadOnly(string path, string sid)
    {
        DirectoryInfo directory = new(path);
        DirectorySecurity security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(sid),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}
