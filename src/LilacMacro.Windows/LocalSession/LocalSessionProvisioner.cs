using System.Security.Principal;
using LilacMacro.Core.LocalSession;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

public sealed class LocalSessionProvisioner(LocalSessionPaths paths)
{
    private const string RunnerName = "LilacMacroRunner";
    private readonly ProvisioningJournalStore journalStore = new(paths);
    private readonly LocalSessionStatusStore statusStore = new(paths);
    private readonly RunnerCredentialManager credentials = new();
    private readonly RunnerAccountManager accounts = new();
    private readonly RunnerScheduledTaskManager tasks = new();
    private readonly FirewallIsolationManager firewall = new();
    private readonly TermServiceConfigurationManager termService = new(paths);
    private readonly LocalSessionCleanupVerifier cleanupVerifier = new(paths, new RunnerAccountManager(), new RunnerCredentialManager(), new RunnerScheduledTaskManager(), new FirewallIsolationManager());

    public async Task InstallOrRepairAsync(string appVersion, bool repair, CancellationToken cancellationToken)
    {
        EnsureElevated();
        string ownerSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Owner SID is unavailable.");
        LocalSessionProvisioningManifest? existing = await journalStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!repair && existing is not null) throw new InvalidOperationException("A provisioning journal already exists. Use repair or remove.");
        if (!repair && accounts.Exists(RunnerName)) throw new InvalidOperationException("LilacMacroRunner already exists but is not owned by this installation.");
        if (repair && existing is null) throw new InvalidOperationException("No owned local runner exists to repair. Use setup instead.");
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

        await statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.Installing, StatusCode = repair ? "repairing" : "installing", Detail = "Provisioning the optional local runner." }, cancellationToken).ConfigureAwait(false);
        NativePayloadVerification payload;
        LocalSessionCompatibilityResult compatibility;
        try
        {
            if (!repair && credentials.Exists(paths.CredentialTarget))
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
                NativePayloadVersion = payload.Version,
                NativePayload = payload.Files,
                CompatibilityEvidence = compatibility.Evidence,
                OriginalSystemState = RegistryStateJournal.Capture(mutations),
                OwnedResources =
                [
                    new("local-account", RunnerName), new("credential", paths.CredentialTarget),
                    new("scheduled-task", RunnerScheduledTaskManager.TaskName),
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
                Detail = "Local runner setup stopped before Windows was changed.",
                Problems = [preparationError.Message],
            }, cancellationToken).ConfigureAwait(false);
            throw;
        }

        try
        {
            LocalSessionAclManager.SecureDirectory(paths.SessionRoot, ownerSid, SystemSid(), AdministratorsSid());
            string password = repair && accounts.Exists(RunnerName)
                ? ReadOrRotateCredential()
                : RunnerCredentialManager.CreateRandomPassword();
            string runnerSid = accounts.EnsureCreated(RunnerName, password);
            accounts.SetPassword(RunnerName, password);
            manifest = manifest with { RunnerSid = runnerSid, CompletedSteps = AddStep(manifest, "account-created") };
            await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
            LocalSessionAclManager.SecureSessionRoots(paths, ownerSid, runnerSid);
            credentials.Write(paths.CredentialTarget, $".\\{RunnerName}", password);
            manifest = await RecordAsync(manifest, "credential-stored", cancellationToken).ConfigureAwait(false);

            RunnerProfileStore profileStore = new(paths);
            DeleteIfPresent(paths.ProfileReceiptPath);
            DeleteIfPresent(paths.ProfileFailurePath);
            await profileStore.WritePolicyAsync(new RunnerProfilePolicy(), cancellationToken).ConfigureAwait(false);
            int profileExit = await new RunnerProcessLauncher().RunAndWaitAsync(
                RunnerName, password, paths.WorkerPath,
                ["--apply-profile-policy", paths.ProfilePolicyPath, paths.ProfileReceiptPath],
                TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            if (profileExit != 0)
            {
                RunnerProfileFailure? failure = await profileStore.ReadFailureAsync(cancellationToken).ConfigureAwait(false);
                string detail = failure is null
                    ? "The controlled runner profile policy pass failed without a diagnostic receipt."
                    : $"The controlled runner profile policy pass failed ({failure.FailureCode}: {failure.Detail}).";
                throw new InvalidOperationException(detail);
            }
            RunnerProfileReceipt? receipt = await profileStore.ReadReceiptAsync(cancellationToken).ConfigureAwait(false);
            if (receipt is null || receipt.RunnerSid != runnerSid || receipt.PolicyVersion != RunnerProfilePolicy.CurrentVersion)
                throw new InvalidDataException("The controlled runner profile could not be verified.");
            manifest = await RecordAsync(manifest, "profile-verified", cancellationToken).ConfigureAwait(false);

            manifest = await RecordAsync(manifest, "term-service-mutation-started", cancellationToken).ConfigureAwait(false);
            termService.Apply();
            await firewall.InstallAsync(cancellationToken).ConfigureAwait(false);
            termService.Restart();
            FirewallIsolationVerification isolation = await firewall.VerifyLoopbackOnlyAsync(cancellationToken).ConfigureAwait(false);
            if (!isolation.Passed)
                throw new InvalidOperationException($"Loopback-only RDP isolation could not be verified: {isolation.Problem}");
            manifest = await RecordAsync(manifest, "loopback-isolation-verified", cancellationToken).ConfigureAwait(false);
            tasks.Register(RunnerName, password, paths.WorkerPath);
            manifest = await RecordAsync(manifest, "worker-task-registered", cancellationToken).ConfigureAwait(false);
            manifest = manifest with { State = LocalSessionState.Degraded, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
            await statusStore.WriteAsync(new LocalSessionStatus
            {
                State = LocalSessionState.Degraded,
                StatusCode = "awaiting-runner-capture",
                Detail = "Provisioning passed. Connect the local runner once so fresh WGC capture can be verified.",
                CompatibilityPassed = true,
                LoopbackIsolationPassed = true,
                RuntimeHostPassed = false,
                PolicyVersion = RunnerProfilePolicy.CurrentVersion,
                WorkerVersion = appVersion,
                Problems = ["Fresh runner capture has not been verified."],
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
                Detail = "Local runner setup failed and all recorded changes were rolled back.",
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
        await statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.Removing, StatusCode = "removing", Detail = "Removing the local runner and restoring Windows." }, cancellationToken).ConfigureAwait(false);
        try
        {
            await RemoveInternalAsync(manifest, cancellationToken).ConfigureAwait(false);
            if (purgeStatusAfterSuccess && Directory.Exists(paths.SessionRoot)) Directory.Delete(paths.SessionRoot, recursive: true);
        }
        catch (Exception error)
        {
            await statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.RecoveryRequired, StatusCode = "cleanup-incomplete", Detail = "Local runner cleanup requires retry.", Problems = [error.Message] }, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RemoveInternalAsync(LocalSessionProvisioningManifest manifest, CancellationToken cancellationToken)
    {
        List<string> failures = [];
        Attempt(failures, "runner session logoff", () => RunnerSessionManager.LogoffAll(RunnerName));
        Attempt(failures, "scheduled-task removal", tasks.Remove);
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

        Attempt(failures, "credential removal", () => credentials.Delete(paths.CredentialTarget));
        string? profilePath = null;
        Attempt(failures, "runner profile lookup", () => profilePath = GetProfilePath(manifest.RunnerSid));
        Attempt(failures, "runner account removal", () => accounts.Remove(RunnerName, profilePath));
        Attempt(failures, "runner data removal", DeleteOwnedRunnerDirectory);
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

    private static string? GetProfilePath(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid)) return null;
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        return key?.GetValue("ProfileImagePath") is string value ? Environment.ExpandEnvironmentVariables(value) : null;
    }

    private void DeleteOwnedRunnerDirectory()
    {
        string root = Path.GetFullPath(paths.RunnerRoot);
        string parent = Path.GetFullPath(paths.ProgramDataRoot) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Runner path escaped the owned ProgramData root.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private string ReadOrRotateCredential()
    {
        try { return credentials.ReadPassword(paths.CredentialTarget); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return RunnerCredentialManager.CreateRandomPassword();
        }
    }

    private Task WriteRecoveryAsync(Exception setup, Exception rollback, CancellationToken cancellationToken) =>
        statusStore.WriteAsync(new LocalSessionStatus { State = LocalSessionState.RecoveryRequired, StatusCode = "rollback-incomplete", Detail = "Setup failed and cleanup requires retry.", Problems = [setup.Message, rollback.Message] }, cancellationToken);
}
