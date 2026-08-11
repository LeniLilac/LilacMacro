using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed class LocalSessionStatusStore(LocalSessionPaths paths)
{
    public async Task<LocalSessionStatus> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await AtomicJsonFile.ReadAsync<LocalSessionStatus>(paths.StatusPath, cancellationToken)
                    .ConfigureAwait(false)
                ?? new LocalSessionStatus();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new LocalSessionStatus
            {
                State = LocalSessionState.RecoveryRequired,
                StatusCode = "status-unreadable",
                Detail = "Local session status could not be validated.",
                Problems = [exception.Message],
            };
        }
    }

    public Task WriteAsync(LocalSessionStatus status, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(paths.StatusPath, status, cancellationToken);
}

public sealed class ProvisioningJournalStore(LocalSessionPaths paths)
{
    public Task<LocalSessionProvisioningManifest?> ReadAsync(CancellationToken cancellationToken = default) =>
        AtomicJsonFile.ReadAsync<LocalSessionProvisioningManifest>(paths.JournalPath, cancellationToken);

    public Task WriteAsync(LocalSessionProvisioningManifest manifest, CancellationToken cancellationToken = default)
    {
        LocalSessionValidationResult validation = LocalSessionValidation.Validate(manifest);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return AtomicJsonFile.WriteAsync(paths.JournalPath, manifest, cancellationToken);
    }
}

public sealed class RunnerSnapshotStore(LocalSessionPaths paths)
{
    public async Task PublishAsync(
        RunnerRuntimeSnapshot snapshot,
        string expectedOwnerSid,
        CancellationToken cancellationToken = default)
    {
        LocalSessionValidationResult validation = LocalSessionValidation.Validate(snapshot, expectedOwnerSid);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        await AtomicJsonFile.WriteAsync(paths.SnapshotPath, snapshot, cancellationToken).ConfigureAwait(false);
    }

    public Task<RunnerRuntimeSnapshot?> ReadAsync(CancellationToken cancellationToken = default) =>
        AtomicJsonFile.ReadAsync<RunnerRuntimeSnapshot>(paths.SnapshotPath, cancellationToken);
}

public sealed class RunnerProfileStore(LocalSessionPaths paths)
{
    public Task WritePolicyAsync(RunnerProfilePolicy policy, CancellationToken cancellationToken = default)
    {
        LocalSessionValidationResult validation = LocalSessionValidation.Validate(policy);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return AtomicJsonFile.WriteAsync(paths.ProfilePolicyPath, policy, cancellationToken);
    }

    public Task<RunnerProfilePolicy?> ReadPolicyAsync(CancellationToken cancellationToken = default) =>
        AtomicJsonFile.ReadAsync<RunnerProfilePolicy>(paths.ProfilePolicyPath, cancellationToken);

    public Task WriteReceiptAsync(RunnerProfileReceipt receipt, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(paths.ProfileReceiptPath, receipt, cancellationToken);

    public Task<RunnerProfileReceipt?> ReadReceiptAsync(CancellationToken cancellationToken = default) =>
        AtomicJsonFile.ReadAsync<RunnerProfileReceipt>(paths.ProfileReceiptPath, cancellationToken);
}
