using System.Security.AccessControl;
using System.Security.Principal;

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
        SecureDirectory(paths.RunnerRoot, ownerSid, runnerSid, systemSid, adminsSid);
    }
}
