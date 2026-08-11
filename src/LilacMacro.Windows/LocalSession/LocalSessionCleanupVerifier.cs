using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed class LocalSessionCleanupVerifier(
    LocalSessionPaths paths,
    RunnerAccountManager accounts,
    RunnerCredentialManager credentials,
    RunnerScheduledTaskManager tasks,
    FirewallIsolationManager firewall)
{
    public IReadOnlyList<string> Inspect(LocalSessionProvisioningManifest manifest)
    {
        List<string> unresolved = [];
        if (accounts.Exists(manifest.RunnerAccountName)) unresolved.Add($"Local account remains: {manifest.RunnerAccountName}");
        if (credentials.Exists(paths.CredentialTarget)) unresolved.Add($"Credential remains: {paths.CredentialTarget}");
        if (tasks.Exists()) unresolved.Add($"Scheduled task remains: {RunnerScheduledTaskManager.TaskName}");
        if (firewall.RulesExist()) unresolved.Add("One or more LilacMacro local-session firewall rules remain.");
        if (Directory.Exists(paths.RunnerRoot)) unresolved.Add($"Runner data remains: {paths.RunnerRoot}");
        unresolved.AddRange(RegistryStateJournal.FindRestoreMismatches(manifest.OriginalSystemState));
        return unresolved;
    }
}

public sealed class LocalSessionCleanupException(IReadOnlyList<string> unresolved)
    : InvalidOperationException("Local runner cleanup is incomplete: " + string.Join("; ", unresolved))
{
    public IReadOnlyList<string> UnresolvedResources { get; } = unresolved;
}
