using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed class LocalSessionCleanupVerifier(
    LocalSessionPaths paths,
    RunnerAccountManager accounts,
    RunnerCredentialManager credentials,
    RunnerScheduledTaskManager tasks,
    FirewallIsolationManager firewall,
    RemoteDesktopCertificateManager rdpCertificates)
{
    public IReadOnlyList<string> Inspect(LocalSessionProvisioningManifest manifest)
    {
        List<string> unresolved = [];
        IReadOnlyList<LocalRunnerProfile> profiles = manifest.RunnerProfiles.Count > 0
            ? manifest.RunnerProfiles
            : [LocalRunnerProfileProvisioner.Create(1, RunnerConfigurationMode.Shared) with
                { AccountName = manifest.RunnerAccountName, RunnerSid = manifest.RunnerSid }];
        foreach (LocalRunnerProfile profile in profiles)
        {
            if (accounts.Exists(profile.AccountName)) unresolved.Add($"Local account remains: {profile.AccountName}");
            if (credentials.ExistsRdp(paths.CredentialTargetFor(profile)))
                unresolved.Add($"Credential remains: {paths.CredentialTargetFor(profile)}");
            if (credentials.ExistsSecret(paths.SecretCredentialTargetFor(profile)))
                unresolved.Add($"Credential remains: {paths.SecretCredentialTargetFor(profile)}");
            if (tasks.Exists(profile.Id)) unresolved.Add($"Scheduled task remains: {RunnerScheduledTaskManager.TaskNameFor(profile.Id)}");
            if (profile.ConfigurationMode == RunnerConfigurationMode.Isolated && Directory.Exists(paths.ConfigurationRootFor(profile)))
                unresolved.Add($"Isolated configuration remains: {paths.ConfigurationRootFor(profile)}");
        }
        if (credentials.ExistsRdp(paths.CredentialTarget)) unresolved.Add($"Credential remains: {paths.CredentialTarget}");
        if (credentials.ExistsSecret(paths.SecretCredentialTarget)) unresolved.Add($"Credential remains: {paths.SecretCredentialTarget}");
        if (credentials.Exists(paths.PortCredentialTarget)) unresolved.Add($"Credential remains: {paths.PortCredentialTarget}");
        if (credentials.Exists(paths.LegacyCredentialTarget)) unresolved.Add($"Credential remains: {paths.LegacyCredentialTarget}");
        if (tasks.LegacyWorkerTaskExists()) unresolved.Add($"Scheduled task remains: {RunnerScheduledTaskManager.LegacyTaskName}");
        if (firewall.RulesExist()) unresolved.Add("One or more LilacMacro local-session firewall rules remain.");
        if (Directory.Exists(paths.RunnerRoot)) unresolved.Add($"Runner data remains: {paths.RunnerRoot}");
        if (Directory.Exists(paths.ProfilesRoot)) unresolved.Add($"Instance profile data remains: {paths.ProfilesRoot}");
        unresolved.AddRange(RegistryStateJournal.FindRestoreMismatches(manifest.OriginalSystemState));
        unresolved.AddRange(rdpCertificates.FindRestoreMismatches(manifest.OriginalSystemState));
        return unresolved;
    }
}

public sealed class LocalSessionCleanupException(IReadOnlyList<string> unresolved)
    : InvalidOperationException("Local instance cleanup is incomplete: " + string.Join("; ", unresolved))
{
    public IReadOnlyList<string> UnresolvedResources { get; } = unresolved;
}
