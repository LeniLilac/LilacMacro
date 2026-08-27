using System.Diagnostics;
using System.Text;
using LilacMacro.App.Diagnostics;
using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.App.Runtime;

internal sealed record LocalInstanceProfileStatus(
    LocalRunnerProfile Profile,
    RunnerSessionObservation Session);

internal sealed record LocalInstanceManagerSnapshot(
    LocalSessionStatus Status,
    IReadOnlyList<LocalInstanceProfileStatus> Profiles);

internal sealed class LocalInstanceManagerController
{
    private readonly LocalSessionPaths paths = LocalSessionPaths.CreateDefault(AppContext.BaseDirectory);
    private readonly DeepDebugSessionService deepDebug;

    internal LocalInstanceManagerController(DeepDebugSessionService deepDebug)
    {
        this.deepDebug = deepDebug;
    }

    public async Task<LocalInstanceManagerSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        LocalSessionStatus status = await new LocalSessionStatusStore(paths).ReadAsync(cancellationToken).ConfigureAwait(false);
        status = LocalSessionValidation.ReconcileInterruptedOperation(
            status,
            File.Exists(paths.JournalPath),
            IsSetupHelperRunning());
        if (status.State is (LocalSessionState.Ready or LocalSessionState.Degraded) && File.Exists(paths.JournalPath))
        {
            LocalSessionCompatibilityResult compatibility = await new LocalSessionCompatibilityProbe(paths)
                .ProbeAsync(LocalSessionProbePurpose.Health, cancellationToken).ConfigureAwait(false);
            if (!compatibility.IsCompatible)
            {
                bool ownershipConflict = RemoteDesktopOwnershipPolicy.IsOwnershipConflict(compatibility.Problems);
                status = status with
                {
                    State = LocalSessionState.Degraded,
                    StatusCode = ownershipConflict ? "rdp-ownership-conflict" : "native-compatibility-changed",
                    Detail = ownershipConflict
                        ? "Another RDP configuration owns this machine. LilacMacro will not overwrite it."
                        : "Windows or the pinned native payload changed. Repair must revalidate local instances.",
                    CompatibilityPassed = false,
                    Problems = compatibility.Problems,
                };
            }
        }
        LocalSessionProvisioningManifest? manifest = await new ProvisioningJournalStore(paths)
            .ReadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LocalRunnerProfile> profiles = LocalSessionProfileCompatibility.ResolveProfiles(manifest);
        if (string.Equals(status.StatusCode, "instance-manager-ready", StringComparison.Ordinal))
            status = status with { Detail = $"{profiles.Count} local macro instance(s) configured." };
        return new LocalInstanceManagerSnapshot(
            status,
            profiles.Select(profile => new LocalInstanceProfileStatus(
                profile,
                RunnerSessionManager.Inspect(profile.AccountName))).ToArray());
    }

    public Task<LocalInstanceManagerSnapshot> InstallAsync(CancellationToken cancellationToken = default) =>
        RunHelperAsync(["install"], cancellationToken);

    public Task<LocalInstanceManagerSnapshot> RepairAsync(CancellationToken cancellationToken = default) =>
        RunHelperAsync(["repair"], cancellationToken);

    public Task<LocalInstanceManagerSnapshot> RemoveAllAsync(CancellationToken cancellationToken = default) =>
        RunHelperAsync(["remove"], cancellationToken);

    public Task<LocalInstanceManagerSnapshot> AddAsync(
        RunnerConfigurationMode mode,
        CancellationToken cancellationToken = default) =>
        RunHelperAsync([mode == RunnerConfigurationMode.Shared ? "add-shared" : "add-isolated"], cancellationToken);

    public Task<LocalInstanceManagerSnapshot> RemoveAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        RunHelperAsync(["remove-profile", profileId], cancellationToken);

    public async Task OpenAsync(
        string profileId,
        MacroLayoutProfile layout,
        CancellationToken cancellationToken = default)
    {
        Stopwatch operationTimer = Stopwatch.StartNew();
        LocalInstanceManagerSnapshot? snapshot = null;
        try
        {
            snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!snapshot.Status.CanOpenInteractiveSession)
                throw new InvalidOperationException(snapshot.Status.Problems.FirstOrDefault() ?? snapshot.Status.Detail);
            LocalRunnerProfile profile = snapshot.Profiles.SingleOrDefault(item => item.Profile.Id == profileId)?.Profile
                ?? throw new InvalidOperationException("The requested local instance is not configured.");
            RefreshRdpCredential(profile);
            _ = Process.Start(CreateRdpStartInfo(profile, layout))
                ?? throw new InvalidOperationException("Windows did not start the local instance viewport.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure(
                "open",
                snapshot is null ? "not-applicable" : ConfigurationModeFor(snapshot, profileId),
                operationTimer,
                snapshot,
                null,
                helperStarted: true,
                error);
            throw;
        }
    }

    internal static ProcessStartInfo CreateRdpStartInfo(
        LocalRunnerProfile profile,
        MacroLayoutProfile layout = MacroLayoutProfile.Full1920x1080) => new(
            "mstsc.exe",
            $"\"{WriteRdpProfile(profile, layout)}\"")
        {
            UseShellExecute = true,
        };

    internal static ProcessStartInfo CreateRdpStartInfo(
        LocalRunnerProfile profile,
        string rdpRoot,
        MacroLayoutProfile layout = MacroLayoutProfile.Full1920x1080) => new(
            "mstsc.exe",
            $"\"{WriteRdpProfile(profile, layout, rdpRoot)}\"")
        {
            UseShellExecute = true,
        };

    internal static string CreateRdpProfileContent(
        LocalRunnerProfile profile,
        MacroLayoutProfile layout = MacroLayoutProfile.Full1920x1080)
    {
        ValidateRdpProfile(profile);
        (double width, double height) = MacroDisplayPolicy.TargetSize(layout);
        return string.Join(Environment.NewLine,
        [
            "screen mode id:i:1",
            "use multimon:i:0",
            "session bpp:i:32",
            $"desktopwidth:i:{(int)width}",
            $"desktopheight:i:{(int)height}",
            "smart sizing:i:1",
            "dynamic resolution:i:0",
            $"full address:s:{profile.LoopbackAddress}:{TermServiceConfigurationManager.LocalPort}",
            $"username:s:{Environment.MachineName}\\{profile.AccountName}",
            "authentication level:i:0",
            "enablecredsspsupport:i:1",
            "prompt for credentials:i:0",
            "redirectclipboard:i:0",
            "redirectprinters:i:0",
            "redirectcomports:i:0",
            "redirectsmartcards:i:0",
            "redirectwebauthn:i:0",
            "devicestoredirect:s:",
            "drivestoredirect:s:",
            "audiomode:i:2",
        ]) + Environment.NewLine;
    }

    private static string WriteRdpProfile(
        LocalRunnerProfile profile,
        MacroLayoutProfile layout,
        string? rdpRoot = null)
    {
        rdpRoot ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "rdp");
        Directory.CreateDirectory(rdpRoot);
        string path = Path.Combine(rdpRoot, $"{profile.Id}.rdp");
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, CreateRdpProfileContent(profile, layout), Encoding.Unicode);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return path;
    }

    private void RefreshRdpCredential(LocalRunnerProfile profile)
    {
        RunnerCredentialManager credentials = new();
        string password;
        try
        {
            password = credentials.ReadPassword(paths.SecretCredentialTargetFor(profile));
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidDataException)
        {
            throw new InvalidOperationException($"The saved credential for {profile.DisplayName} is unavailable. Run Repair once.", exception);
        }
        string qualifiedRunner = $"{Environment.MachineName}\\{profile.AccountName}";
        credentials.DeleteRdp(paths.CredentialTargetFor(profile));
        credentials.WriteRdp(paths.CredentialTargetFor(profile), qualifiedRunner, password);
    }

    private static void ValidateRdpProfile(LocalRunnerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id.Length is < 1 or > 32
            || !profile.Id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            || !profile.LoopbackAddress.StartsWith("127.0.0.", StringComparison.Ordinal)
            || !int.TryParse(profile.LoopbackAddress[8..], out int host)
            || host is < 2 or > 17)
        {
            throw new InvalidDataException("The local instance RDP profile is invalid.");
        }
    }

    private async Task<LocalInstanceManagerSnapshot> RunHelperAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string operation = LocalInstanceFailurePolicy.OperationFor(arguments);
        string configurationMode = LocalInstanceFailurePolicy.ConfigurationModeFor(operation);
        Stopwatch operationTimer = Stopwatch.StartNew();
        LocalInstanceManagerSnapshot? snapshot = null;
        int? processExitCode = null;
        bool helperStarted = false;
        try
        {
            string helper = Path.Combine(AppContext.BaseDirectory, "LilacMacro.SessionSetup.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("The signed local-instance setup helper is missing.", helper);
            using Process process = Process.Start(new ProcessStartInfo(helper)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
                Arguments = string.Join(' ', arguments.Select(QuoteArgument)),
            }) ?? throw new InvalidOperationException("Windows did not start the local-instance setup helper.");
            helperStarted = true;
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            processExitCode = process.ExitCode;
            snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (processExitCode != 0)
                throw new InvalidOperationException(OperationFailureDetail(snapshot));
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure(
                operation,
                configurationMode,
                operationTimer,
                snapshot,
                processExitCode,
                helperStarted,
                error);
            throw;
        }
    }

    private void RecordFailure(
        string operation,
        string configurationMode,
        Stopwatch operationTimer,
        LocalInstanceManagerSnapshot? snapshot,
        int? processExitCode,
        bool helperStarted,
        Exception error) =>
        deepDebug.RecordEvent("local_instance", "operation_failed", new LocalInstanceFailureObservation(
            operation,
            LocalInstanceFailurePolicy.Classify(
                operation,
                snapshot?.Status.StatusCode,
                error,
                processExitCode,
                helperStarted),
            snapshot?.Status.StatusCode,
            configurationMode,
            LocalInstanceFailurePolicy.BoundedDuration(operationTimer.Elapsed),
            LocalInstanceFailurePolicy.BoundedExitCode(processExitCode),
            LocalInstanceFailurePolicy.BoundedRunnerCount(snapshot?.Profiles.Count ?? 0),
            helperStarted,
            error.GetType().Name,
            error.ToString()));

    private static string ConfigurationModeFor(LocalInstanceManagerSnapshot snapshot, string profileId) =>
        snapshot.Profiles.SingleOrDefault(item => item.Profile.Id == profileId)?.Profile.ConfigurationMode
            == RunnerConfigurationMode.Shared
            ? "shared"
            : snapshot.Profiles.Any(item => item.Profile.Id == profileId)
                ? "isolated"
                : "not-applicable";

    internal static string OperationFailureDetail(LocalInstanceManagerSnapshot snapshot) =>
        snapshot.Status.Problems.FirstOrDefault()
        ?? (string.Equals(snapshot.Status.StatusCode, "instance-manager-ready", StringComparison.Ordinal)
            ? "The local instance operation did not complete. Run Repair and retry it."
            : snapshot.Status.Detail);

    private static string QuoteArgument(string value) => value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        ? value
        : $"\"{value.Replace("\"", "\\\"")}\"";

    private static bool IsSetupHelperRunning()
    {
        Process[] processes = Process.GetProcessesByName("LilacMacro.SessionSetup");
        try { return processes.Length > 0; }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }
}
