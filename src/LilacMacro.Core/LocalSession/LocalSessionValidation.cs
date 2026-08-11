using System.Security.Cryptography;
using System.Text.Json;

namespace LilacMacro.Core.LocalSession;

public sealed record LocalSessionValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static LocalSessionValidationResult Success { get; } = new(true, []);
}

public static class LocalSessionValidation
{
    public static LocalSessionValidationResult Validate(LocalSessionProvisioningManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        List<string> errors = [];
        if (manifest.SchemaVersion != LocalSessionProvisioningManifest.CurrentSchemaVersion)
            errors.Add("Unsupported provisioning manifest schema.");
        if (!IsSid(manifest.OwnerSid)) errors.Add("Owner SID is invalid.");
        if (manifest.RunnerSid.Length > 0 && !IsSid(manifest.RunnerSid)) errors.Add("Runner SID is invalid.");
        if (!string.Equals(manifest.RunnerAccountName, "LilacMacroRunner", StringComparison.Ordinal))
            errors.Add("Runner account name is not owned by LilacMacro.");
        if (manifest.NativePayload.GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add("Native payload contains duplicate paths.");
        foreach (NativePayloadFile file in manifest.NativePayload)
        {
            if (!IsSafeRelativePath(file.RelativePath)) errors.Add($"Unsafe payload path: {file.RelativePath}");
            if (file.Size <= 0) errors.Add($"Invalid payload size: {file.RelativePath}");
            if (!IsSha256(file.Sha256)) errors.Add($"Invalid payload hash: {file.RelativePath}");
        }
        if (manifest.CompatibilityEvidence is { } evidence)
        {
            if (evidence.SchemaVersion != LocalSessionCompatibilityEvidence.CurrentSchemaVersion)
                errors.Add("Unsupported native compatibility evidence schema.");
            if (string.IsNullOrWhiteSpace(evidence.ProbeVersion)) errors.Add("Compatibility evidence has no probe version.");
            if (string.IsNullOrWhiteSpace(evidence.OsBuild)) errors.Add("Compatibility evidence has no OS build identity.");
            if (!string.Equals(evidence.Architecture, "X64", StringComparison.Ordinal))
                errors.Add("Compatibility evidence has an unsupported architecture.");
            if (!IsSha256(evidence.TermServiceSha256)) errors.Add("Compatibility evidence has an invalid TermService hash.");
            if (!IsSha256(evidence.TermWrapSha256)) errors.Add("Compatibility evidence has an invalid TermWrap hash.");
            if (!evidence.RequiredPatchesPassed || evidence.RequiredPatchDiagnostics.Count > 0)
                errors.Add("Compatibility evidence does not prove every required native patch.");
        }
        return Result(errors);
    }

    public static LocalSessionValidationResult Validate(RunnerProfilePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        List<string> errors = [];
        if (!string.Equals(policy.Version, RunnerProfilePolicy.CurrentVersion, StringComparison.Ordinal))
            errors.Add("Unsupported runner policy version.");
        if (policy.PackageRules.Any(rule => rule.PackageFamilyName.Contains('*', StringComparison.Ordinal)))
            errors.Add("Runner package policy may not contain wildcard removals.");
        if (policy.PackageRules.GroupBy(rule => rule.PackageFamilyName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add("Runner package policy contains duplicates.");
        foreach (RunnerRegistryRule rule in policy.RegistryRules)
        {
            if (rule.RelativeKey.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
                errors.Add("Runner policy registry paths must be relative to the runner hive.");
            if (rule.RelativeKey.Contains("Defender", StringComparison.OrdinalIgnoreCase))
                errors.Add("Runner policy may not modify Defender.");
            if (!rule.DeleteWhenPresent && rule.ValueKind is not ("DWord" or "String"))
                errors.Add($"Runner policy has an unsupported value kind: {rule.ValueKind}.");
        }
        return Result(errors);
    }

    public static LocalSessionValidationResult Validate(
        RunnerRuntimeSnapshot snapshot,
        string expectedOwnerSid,
        string? expectedAppVersion = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<string> errors = [];
        if (snapshot.SchemaVersion != RunnerRuntimeSnapshot.CurrentSchemaVersion)
            errors.Add("Unsupported runtime snapshot schema.");
        if (snapshot.Revision <= 0) errors.Add("Snapshot revision must be positive.");
        if (!string.Equals(snapshot.OwnerSid, expectedOwnerSid, StringComparison.Ordinal))
            errors.Add("Snapshot owner SID does not match the provisioned owner.");
        if (snapshot.ExecutionTarget != ExecutionTarget.LocalRunnerSession)
            errors.Add("Runner snapshot has the wrong execution target.");
        if (!string.IsNullOrWhiteSpace(expectedAppVersion)
            && !string.Equals(snapshot.AppVersion, expectedAppVersion, StringComparison.Ordinal))
            errors.Add("Snapshot application version does not match the worker.");
        if (string.IsNullOrWhiteSpace(snapshot.PlanName)) errors.Add("Snapshot plan name is missing.");
        if (snapshot.Tasks.Count == 0) errors.Add("Snapshot has no runnable tasks.");
        if (snapshot.Tasks.Select(task => task.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Tasks.Count)
            errors.Add("Snapshot task identifiers are not unique.");
        foreach (RunnerTaskSnapshot task in snapshot.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id)) errors.Add("Snapshot task identifier is missing.");
            if (string.IsNullOrWhiteSpace(task.Route) && task.Mode != RunnerTaskMode.Challenge)
                errors.Add($"Snapshot task {task.Id} has no route.");
            if (task.Target < 1) errors.Add($"Snapshot task {task.Id} has an invalid target.");
            if (task.DefeatRetries is < 0 or > 20) errors.Add($"Snapshot task {task.Id} has invalid defeat retries.");
        }
        if (snapshot.PlacementSetups.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            errors.Add("Snapshot placement setups are missing.");
        if (snapshot.StateContexts.Count == 0) errors.Add("Snapshot state contexts are missing.");
        if (snapshot.StateContexts.GroupBy(context => context.State, StringComparer.Ordinal).Any(group => group.Count() > 1))
            errors.Add("Snapshot state contexts contain duplicate states.");
        foreach (RunnerStateContextSnapshot context in snapshot.StateContexts)
        {
            if (string.IsNullOrWhiteSpace(context.State)) errors.Add("Snapshot state context has no name.");
            if (context.RegionOfInterest.Width <= 0 || context.RegionOfInterest.Height <= 0)
                errors.Add($"Snapshot state context {context.State} has invalid bounds.");
        }
        return Result(errors);
    }

    public static bool IsTransitionAllowed(LocalSessionState from, LocalSessionState to) => (from, to) switch
    {
        (LocalSessionState.Absent, LocalSessionState.Installing) => true,
        (LocalSessionState.Installing, LocalSessionState.Ready or LocalSessionState.Degraded or LocalSessionState.RecoveryRequired) => true,
        (LocalSessionState.Ready, LocalSessionState.Degraded or LocalSessionState.Removing) => true,
        (LocalSessionState.Degraded, LocalSessionState.Ready or LocalSessionState.Installing or LocalSessionState.Removing or LocalSessionState.RecoveryRequired) => true,
        (LocalSessionState.RecoveryRequired, LocalSessionState.Installing or LocalSessionState.Removing) => true,
        (LocalSessionState.Removing, LocalSessionState.Absent or LocalSessionState.RecoveryRequired) => true,
        _ => from == to,
    };

    public static LocalSessionStatus ReconcileInterruptedOperation(
        LocalSessionStatus status,
        bool journalExists,
        bool helperActive)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (helperActive || status.State is not (LocalSessionState.Installing or LocalSessionState.Removing)) return status;
        bool recoveryRequired = journalExists;
        return status with
        {
            State = recoveryRequired ? LocalSessionState.RecoveryRequired : LocalSessionState.Absent,
            StatusCode = "setup-interrupted",
            Detail = recoveryRequired
                ? "Local-session setup was interrupted after Windows changes were recorded. Run Remove or Repair."
                : "Local-session setup was interrupted before Windows was changed. Set Up can be retried.",
            Problems = status.Problems.Count > 0
                ? status.Problems
                : ["The elevated local-session helper is no longer running."],
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public static bool FixedTimeHashEquals(string expectedHex, ReadOnlySpan<byte> actualBytes)
    {
        if (!IsSha256(expectedHex)) return false;
        byte[] expected = Convert.FromHexString(expectedHex);
        return CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(actualBytes));
    }

    private static LocalSessionValidationResult Result(List<string> errors) =>
        errors.Count == 0 ? LocalSessionValidationResult.Success : new(false, errors);

    private static bool IsSid(string value)
    {
        string[] parts = value.Split('-');
        return parts.Length >= 3
            && string.Equals(parts[0], "S", StringComparison.Ordinal)
            && string.Equals(parts[1], "1", StringComparison.Ordinal)
            && parts.Skip(2).All(part => part.Length > 0 && part.All(char.IsDigit));
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsSafeRelativePath(string value) =>
        value.Length > 0
        && !Path.IsPathRooted(value)
        && !value.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..", StringComparer.Ordinal);
}
