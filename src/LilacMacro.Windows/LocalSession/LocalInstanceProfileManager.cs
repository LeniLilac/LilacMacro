using System.Security.Principal;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed class LocalInstanceProfileManager(LocalSessionPaths paths)
{
    private readonly ProvisioningJournalStore journalStore = new(paths);
    private readonly LocalSessionStatusStore statusStore = new(paths);
    private readonly RunnerAccountManager accounts = new();
    private readonly LocalRunnerProfileProvisioner profiles = new(paths);

    public async Task AddAsync(
        string appVersion,
        RunnerConfigurationMode configurationMode,
        CancellationToken cancellationToken)
    {
        EnsureElevated();
        string ownerSid = CurrentSid();
        LocalSessionProvisioningManifest manifest = await ReadRequiredAsync(cancellationToken).ConfigureAwait(false);
        EnsureOwner(manifest, ownerSid);
        int slot = Enumerable.Range(1, 16)
            .FirstOrDefault(candidate => manifest.RunnerProfiles.All(profile => profile.Slot != candidate));
        if (slot == 0) throw new InvalidOperationException("LilacMacro supports at most 16 local runner instances.");
        LocalRunnerProfile pending = LocalRunnerProfileProvisioner.Create(slot, configurationMode);
        if (accounts.Exists(pending.AccountName))
            throw new InvalidOperationException($"{pending.AccountName} already exists but is not owned by this instance manager.");
        LocalRunnerProfile[] originalProfiles = [.. manifest.RunnerProfiles];
        manifest = manifest with
        {
            State = LocalSessionState.Installing,
            AppVersion = appVersion,
            WorkerVersion = appVersion,
            RunnerProfiles = [.. originalProfiles, pending],
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
        try
        {
            LocalRunnerProfile provisioned = await profiles.ProvisionAsync(
                pending,
                ownerSid,
                manifest.RunnerProfiles,
                repair: false,
                cancellationToken).ConfigureAwait(false);
            manifest = manifest with
            {
                State = LocalSessionState.Ready,
                RunnerProfiles = [.. originalProfiles, provisioned],
                RunnerSid = originalProfiles.FirstOrDefault()?.RunnerSid ?? provisioned.RunnerSid,
                CompletedSteps = AddStep(manifest.CompletedSteps, $"{provisioned.Id}-profile-verified"),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
            await WriteReadyAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { profiles.Remove(pending, removeConfiguration: true); }
            catch { }
            manifest = manifest with
            {
                State = LocalSessionState.Ready,
                RunnerProfiles = originalProfiles,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
            await WriteReadyAsync(manifest, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task RemoveAsync(string profileId, CancellationToken cancellationToken)
    {
        EnsureElevated();
        string ownerSid = CurrentSid();
        LocalSessionProvisioningManifest manifest = await ReadRequiredAsync(cancellationToken).ConfigureAwait(false);
        EnsureOwner(manifest, ownerSid);
        LocalRunnerProfile profile = manifest.RunnerProfiles.SingleOrDefault(item => item.Id == profileId)
            ?? throw new InvalidOperationException("The requested runner profile is not owned by LilacMacro.");
        profiles.Remove(profile, removeConfiguration: true);
        LocalRunnerProfile[] remaining = manifest.RunnerProfiles.Where(item => item.Id != profileId).ToArray();
        LocalSessionAclManager.SecureInstanceRoots(paths, ownerSid, remaining);
        manifest = manifest with
        {
            State = LocalSessionState.Ready,
            RunnerProfiles = remaining,
            RunnerSid = remaining.FirstOrDefault()?.RunnerSid ?? string.Empty,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await journalStore.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
        await WriteReadyAsync(manifest, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LocalSessionProvisioningManifest> ReadRequiredAsync(CancellationToken cancellationToken)
    {
        LocalSessionProvisioningManifest? manifest = await journalStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (manifest is null) throw new InvalidOperationException("The local instance manager is not installed.");
        if (manifest.RunnerProfiles.Count > 0) return manifest;
        return manifest with
        {
            RunnerProfiles = [LocalRunnerProfileProvisioner.Create(1, RunnerConfigurationMode.Shared) with
            {
                AccountName = manifest.RunnerAccountName,
                RunnerSid = manifest.RunnerSid,
            }],
        };
    }

    private Task WriteReadyAsync(LocalSessionProvisioningManifest manifest, CancellationToken cancellationToken) =>
        statusStore.WriteAsync(new LocalSessionStatus
        {
            State = LocalSessionState.Ready,
            StatusCode = "instance-manager-ready",
            Detail = $"{manifest.RunnerProfiles.Count} local macro instance(s) configured.",
            CompatibilityPassed = true,
            LoopbackIsolationPassed = true,
            FreshCapturePassed = false,
            RuntimeHostPassed = true,
            PolicyVersion = manifest.PolicyVersion,
            WorkerVersion = manifest.AppVersion,
        }, cancellationToken);

    private static IReadOnlyList<string> AddStep(IReadOnlyList<string> steps, string step) =>
        steps.Contains(step, StringComparer.Ordinal) ? steps : [.. steps, step];

    private static string CurrentSid() => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("Owner SID is unavailable.");

    private static void EnsureOwner(LocalSessionProvisioningManifest manifest, string ownerSid)
    {
        if (!string.Equals(manifest.OwnerSid, ownerSid, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Only the Windows account that provisioned this instance manager may change it.");
    }

    private static void EnsureElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Local instance provisioning requires elevation.");
    }
}
