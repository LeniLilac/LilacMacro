using System.Security.Principal;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed class LocalSessionProvisioner(LocalSessionPaths paths)
{
    private const string RunnerName = "LilacMacroRunner";
    private readonly ProvisioningJournalStore journalStore = new(paths);
    private readonly LocalSessionStatusStore statusStore = new(paths);
    private readonly RunnerCredentialManager credentials = new();
    private readonly RunnerAccountManager accounts = new();
    private readonly RunnerScheduledTaskManager tasks = new();
    private readonly LocalRunnerProfileProvisioner profileProvisioner = new(paths);
    private readonly FirewallIsolationManager firewall = new();
    private readonly TermServiceConfigurationManager termService = new(paths);
    private readonly RemoteDesktopCertificateManager rdpCertificates = new();
    private readonly LocalSessionCleanupVerifier cleanupVerifier = new(
        paths,
        new RunnerAccountManager(),
        new RunnerCredentialManager(),
        new RunnerScheduledTaskManager(),
        new FirewallIsolationManager(),
        new RemoteDesktopCertificateManager());

    public async Task InstallOrRepairAsync(string appVersion, bool repair, CancellationToken cancellationToken)
    {
        EnsureElevated();
        string ownerSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Owner SID is unavailable.");
        LocalSessionProvisioningManifest? existing = NormalizeProfiles(
            await journalStore.ReadAsync(cancellationToken).ConfigureAwait(false));
        if (!repair && existing is not null) throw new InvalidOperationException("A provisioning journal already exists. Use repair or remove.");
        if (!repair && accounts.Exists(RunnerName)) throw new InvalidOperationException("LilacMacroRunner already exists but is not owned by this installation.");
        if (repair && existing is null) throw new InvalidOperationException("No owned local instance manager exists to repair. Use setup instead.");
        if (existing is not null)
        {
            if (existing.SchemaVersion != LocalSessionProvisioningManifest.CurrentSchemaVersion)
                throw new InvalidDataException("The provisioning journal schema is not supported by this installer.");
            if (!string.Equals(existing.OwnerSid, ownerSid, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Only the Windows account that provisioned this runner may repair it.");
        }
        if (repair && existing is not null &&
            await FinalizeCompletedRollbackAsync(existing, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.Installing, StatusCode = repair ? "repairing" : "installing", Detail = "Provisioning local macro instances." }, cancellationToken).ConfigureAwait(false);
        NativePayloadVerification payload;
        LocalSessionCompatibilityResult compatibility;
        try
        {
            if (!repair && (credentials.ExistsRdp(paths.CredentialTarget)
                || credentials.ExistsSecret(paths.SecretCredentialTarget)
                || credentials.Exists(paths.PortCredentialTarget)
                || credentials.Exists(paths.LegacyCredentialTarget)))
                throw new InvalidOperationException("An RDP credential already exists for the owned loopback endpoint; LilacMacro will not overwrite it.");
            payload = await new NativePayloadVerifier(paths).VerifyAsync(cancellationToken).ConfigureAwait(false);
            if (!payload.IsValid) throw new InvalidDataException(string.Join(" ", payload.Errors));
            compatibility = await new LocalSessionCompatibilityProbe(paths)
                .ProbeAsync(repair ? LocalSessionProbePurpose.Repair : LocalSessionProbePurpose.Install, cancellationToken)
                .ConfigureAwait(false);
            if (!compatibility.IsCompatible) throw new PlatformNotSupportedException(string.Join(" ", compatibility.Problems));
        }
        catch (Exception preflightError)
        {
            await statusStore.WriteAsync(new LocalSessionStatus
            {
                State = existing is null ? LocalSessionState.Absent : LocalSessionState.Degraded,
                StatusCode = "preflight-rejected",
                Detail = "Windows was not changed because local-session preflight did not pass.",
                Problems = [preflightError.Message],
            }, cancellationToken).ConfigureAwait(false);
            throw;
        }

        IReadOnlyList<RegistryMutation> mutations = termService.GetMutations();
        LocalSessionProvisioningManifest manifest;
        try
        {
            manifest = existing is null ? new LocalSessionProvisioningManifest
            {
                State = LocalSessionState.Installing,
                OwnerSid = ownerSid,
                OsBuild = compatibility.OsBuild,
                AppVersion = appVersion,
                WorkerVersion = appVersion,
                PolicyVersion = RunnerProfilePolicy.CurrentVersion,
                RunnerProfiles = [LocalRunnerProfileProvisioner.Create(1, RunnerConfigurationMode.Shared)],
                NativePayloadVersion = payload.Version,
                NativePayload = payload.Files,
                CompatibilityEvidence = compatibility.Evidence,
                OriginalSystemState =
                [
                    .. RegistryStateJournal.Capture(mutations),
                    .. rdpCertificates.CaptureBaseline(),
                ],
                OwnedResources =
                [
                    new("local-account", RunnerName), new("credential", paths.CredentialTarget),
                    new("credential", paths.SecretCredentialTarget),
                    new("scheduled-task", RunnerScheduledTaskManager.LegacyTaskName),
                    new("firewall-rule", FirewallIsolationManager.TcpRule), new("firewall-rule", FirewallIsolationManager.UdpRule),
                ],
            } : existing with
            {
                State = LocalSessionState.Installing,
                OsBuild = compatibility.OsBuild,
                AppVersion = appVersion,
                WorkerVersion = appVersion,
                PolicyVersion = RunnerProfilePolicy.CurrentVersion,
                NativePayloadVersion = payload.Version,
                NativePayload = payload.Files,
                CompatibilityEvidence = compatibility.Evidence,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            manifest = manifest with { CompletedSteps = AddStep(manifest, "native-preflight-passed") };
            await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception preparationError)
        {
            await statusStore.WriteAsync(new LocalSessionStatus
            {
                State = existing is null ? LocalSessionState.Absent : LocalSessionState.Degraded,
                StatusCode = "setup-preparation-failed",
                Detail = "Local instance setup stopped before Windows was changed.",
                Problems = [preparationError.Message],
            }, cancellationToken).ConfigureAwait(false);
            throw;
        }

        try
        {
            LocalSessionAclManager.SecureDirectory(paths.SessionRoot, ownerSid, SystemSid(), AdministratorsSid());
            List<LocalRunnerProfile> provisionedProfiles = [.. manifest.RunnerProfiles];
            for (int index = 0; index < provisionedProfiles.Count; index++)
            {
                LocalRunnerProfile provisioned = await profileProvisioner.ProvisionAsync(
                    provisionedProfiles[index],
                    ownerSid,
                    provisionedProfiles,
                    repair,
                    cancellationToken).ConfigureAwait(false);
                provisionedProfiles[index] = provisioned;
                manifest = manifest with
                {
                    RunnerProfiles = [.. provisionedProfiles],
                    RunnerSid = provisionedProfiles[0].RunnerSid,
                    CompletedSteps = AddStep(manifest, $"{provisioned.Id}-profile-verified"),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
            }
            tasks.RemoveLegacyWorkerTask();

            manifest = await RecordAsync(manifest, "term-service-mutation-started", cancellationToken).ConfigureAwait(false);
            await firewall.InstallAsync(cancellationToken).ConfigureAwait(false);
            rdpCertificates.RemoveCertificatesWithMissingKeys(manifest.OriginalSystemState);
            termService.ApplyAndRestart(manifest.OriginalSystemState);
            FirewallIsolationVerification isolation = await firewall.VerifyLoopbackOnlyAsync(cancellationToken).ConfigureAwait(false);
            if (!isolation.Passed)
            {
                if (FirewallIsolationManager.IsListenerStartupDelay(isolation))
                {
                    manifest = manifest with
                    {
                        State = LocalSessionState.Degraded,
                        CompletedSteps = AddStep(manifest, "windows-restart-required"),
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    };
                    await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
                    await statusStore.WriteAsync(new LocalSessionStatus
                    {
                        State = LocalSessionState.Degraded,
                        StatusCode = "windows-restart-required",
                        Detail = "Windows must restart once to finish initializing the local RDP listener. Restart Windows, then run Repair.",
                        CompatibilityPassed = true,
                        LoopbackIsolationPassed = false,
                        RuntimeHostPassed = false,
                        PolicyVersion = RunnerProfilePolicy.CurrentVersion,
                        WorkerVersion = appVersion,
                        Problems = [isolation.Problem],
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }
                throw new InvalidOperationException($"Loopback-only RDP isolation could not be verified: {isolation.Problem}");
            }
            manifest = await RecordAsync(manifest, "loopback-isolation-verified", cancellationToken).ConfigureAwait(false);
            manifest = await RecordAsync(manifest, "instance-ui-tasks-registered", cancellationToken).ConfigureAwait(false);
            manifest = manifest with { State = LocalSessionState.Ready, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
            await statusStore.WriteAsync(new LocalSessionStatus
            {
                State = LocalSessionState.Ready,
                StatusCode = "instance-manager-ready",
                Detail = $"{manifest.RunnerProfiles.Count} local macro instance(s) configured.",
                CompatibilityPassed = true,
                LoopbackIsolationPassed = true,
                FreshCapturePassed = false,
                RuntimeHostPassed = true,
                PolicyVersion = RunnerProfilePolicy.CurrentVersion,
                WorkerVersion = appVersion,
                Problems = [],
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception setupError)
        {
            try { await RemoveInternalAsync(manifest, cancellationToken).ConfigureAwait(false); }
            catch (Exception rollbackError)
            {
                await WriteRecoveryAsync(setupError, rollbackError, cancellationToken).ConfigureAwait(false);
                throw new AggregateException("Local session setup and rollback both failed.", setupError, rollbackError);
            }
            await statusStore.WriteAsync(new LocalSessionStatus
            {
                State = LocalSessionState.Absent,
                StatusCode = "setup-failed-rolled-back",
                Detail = "Local instance setup failed and all recorded changes were rolled back.",
                Problems = [setupError.Message],
            }, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public Task RemoveAsync(CancellationToken cancellationToken) => RemoveAsync(purgeStatusAfterSuccess: false, cancellationToken);

    public async Task RecordUnhandledFailureAsync(string verb, Exception error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        ArgumentNullException.ThrowIfNull(error);
        LocalSessionStatus current = await statusStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (current.State == LocalSessionState.Ready) return;
        if (current.State is not (LocalSessionState.Installing or LocalSessionState.Removing)
            && current.Problems.Count > 0) return;

        bool recoveryRequired = File.Exists(paths.JournalPath);
        await statusStore.WriteAsync(new LocalSessionStatus
        {
            State = recoveryRequired ? LocalSessionState.RecoveryRequired : LocalSessionState.Absent,
            StatusCode = "setup-helper-failed",
            Detail = recoveryRequired
                ? "The local-session helper stopped before cleanup was verified. Run Remove or Repair."
                : "The local-session helper stopped before Windows was changed.",
            Problems = [$"{verb}: {error.Message}"],
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(bool purgeStatusAfterSuccess, CancellationToken cancellationToken)
    {
        EnsureElevated();
        LocalSessionProvisioningManifest? manifest = await journalStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (manifest is null) { await statusStore.WriteAsync(new LocalSessionStatus(), cancellationToken).ConfigureAwait(false); return; }
        await statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.Removing, StatusCode = "removing", Detail = "Removing local instances and restoring Windows." }, cancellationToken).ConfigureAwait(false);
        try
        {
            await RemoveInternalAsync(manifest, cancellationToken).ConfigureAwait(false);
            if (purgeStatusAfterSuccess && Directory.Exists(paths.SessionRoot)) Directory.Delete(paths.SessionRoot, recursive: true);
        }
        catch (Exception error)
        {
            await statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.RecoveryRequired, StatusCode = "cleanup-incomplete", Detail = "Local instance cleanup requires retry.", Problems = [error.Message] }, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RemoveInternalAsync(LocalSessionProvisioningManifest manifest, CancellationToken cancellationToken)
    {
        manifest = NormalizeProfiles(manifest)!;
        List<string> failures = [];
        foreach (LocalRunnerProfile profile in manifest.RunnerProfiles)
            Attempt(failures, $"{profile.DisplayName} removal", () => profileProvisioner.Remove(profile, removeConfiguration: true));
        Attempt(failures, "legacy runner task removal", tasks.RemoveLegacyWorkerTask);
        await AttemptAsync(failures, "firewall removal", () => firewall.RemoveAsync(cancellationToken)).ConfigureAwait(false);

        IReadOnlyList<string> registryMismatches;
        try { registryMismatches = RegistryStateJournal.FindRestoreMismatches(manifest.OriginalSystemState); }
        catch (Exception exception)
        {
            failures.Add($"registry inspection: {exception.Message}");
            registryMismatches = ["Registry state could not be inspected."];
        }
        if (RequiresTermServiceRestore(manifest, registryMismatches))
        {
            Attempt(failures, "registry restoration", () => RegistryStateJournal.Restore(manifest.OriginalSystemState));
            Attempt(failures, "TermService restart", termService.Restart);
        }
        Attempt(failures, "RDP certificate restoration", () => rdpCertificates.RestoreBaseline(manifest.OriginalSystemState));

        Attempt(failures, "credential removal", () => credentials.DeleteRdp(paths.CredentialTarget));
        Attempt(failures, "secret credential removal", () => credentials.DeleteSecret(paths.SecretCredentialTarget));
        Attempt(failures, "port-qualified credential removal", () => credentials.Delete(paths.PortCredentialTarget));
        Attempt(failures, "legacy credential removal", () => credentials.Delete(paths.LegacyCredentialTarget));
        Attempt(failures, "runner data removal", DeleteOwnedRunnerDirectory);
        Attempt(failures, "instance profile data removal", DeleteOwnedProfilesDirectory);
        try { failures.AddRange(cleanupVerifier.Inspect(manifest)); }
        catch (Exception exception) { failures.Add($"cleanup verification: {exception.Message}"); }
        if (failures.Count > 0) throw new LocalSessionCleanupException(failures.Distinct(StringComparer.Ordinal).ToArray());
        if (File.Exists(paths.JournalPath)) File.Delete(paths.JournalPath);
        await statusStore.WriteAsync(new LocalSessionStatus(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FinalizeCompletedRollbackAsync(
        LocalSessionProvisioningManifest manifest,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> unresolved;
        try { unresolved = cleanupVerifier.Inspect(manifest); }
        catch { return false; }
        if (unresolved.Count > 0) return false;

        DeleteIfPresent(paths.JournalPath);
        await statusStore.WriteAsync(new LocalSessionStatus(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal static bool RequiresTermServiceRestore(
        LocalSessionProvisioningManifest manifest,
        IReadOnlyList<string> registryMismatches) =>
        manifest.CompletedSteps.Contains("term-service-mutation-started", StringComparer.Ordinal)
        || manifest.CompletedSteps.Contains("loopback-isolation-verified", StringComparer.Ordinal)
        || registryMismatches.Count > 0;

    private static void Attempt(List<string> failures, string step, Action action)
    {
        try { action(); }
        catch (Exception exception) { failures.Add($"{step}: {exception.Message}"); }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static async Task AttemptAsync(List<string> failures, string step, Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add($"{step}: {exception.Message}"); }
    }

    private async Task<LocalSessionProvisioningManifest> RecordAsync(LocalSessionProvisioningManifest manifest, string step, CancellationToken cancellationToken)
    {
        manifest = manifest with { CompletedSteps = AddStep(manifest, step), UpdatedAtUtc = DateTimeOffset.UtcNow };
        await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    private static IReadOnlyList<string> AddStep(LocalSessionProvisioningManifest manifest, string step) => manifest.CompletedSteps.Contains(step, StringComparer.Ordinal) ? manifest.CompletedSteps : [.. manifest.CompletedSteps, step];
    private static string SystemSid() => new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
    private static string AdministratorsSid() => new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
    private static void EnsureElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Local session provisioning requires elevation.");
    }

    private void DeleteOwnedRunnerDirectory()
    {
        string root = Path.GetFullPath(paths.RunnerRoot);
        string parent = Path.GetFullPath(paths.ProgramDataRoot) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Runner path escaped the owned ProgramData root.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private void DeleteOwnedProfilesDirectory()
    {
        string root = Path.GetFullPath(paths.ProfilesRoot);
        string parent = Path.GetFullPath(paths.ProgramDataRoot) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Profile path escaped the owned ProgramData root.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static LocalSessionProvisioningManifest? NormalizeProfiles(LocalSessionProvisioningManifest? manifest)
    {
        if (manifest is null || manifest.RunnerProfiles.Count > 0) return manifest;
        LocalRunnerProfile legacy = LocalRunnerProfileProvisioner.Create(1, RunnerConfigurationMode.Shared) with
        {
            AccountName = manifest.RunnerAccountName,
            RunnerSid = manifest.RunnerSid,
        };
        return manifest with { RunnerProfiles = [legacy] };
    }

    private Task WriteRecoveryAsync(Exception setup, Exception rollback, CancellationToken cancellationToken) =>
        statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.RecoveryRequired, StatusCode = "rollback-incomplete", Detail = "Setup failed and cleanup requires retry.", Problems = [setup.Message, rollback.Message] }, cancellationToken);
}
