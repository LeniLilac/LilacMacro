using System.Diagnostics;
using System.Text;
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
                status = status with
                {
                    State = LocalSessionState.Degraded,
                    StatusCode = "native-compatibility-changed",
                    Detail = "Windows or the pinned native payload changed. Repair must revalidate local instances.",
                    CompatibilityPassed = false,
                    Problems = compatibility.Problems,
                };
            }
        }
        LocalSessionProvisioningManifest? manifest = await new ProvisioningJournalStore(paths)
            .ReadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LocalRunnerProfile> profiles = ProfilesFrom(manifest);
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

    public async Task OpenAsync(string profileId, CancellationToken cancellationToken = default)
    {
        LocalInstanceManagerSnapshot snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Status.CanOpenInteractiveSession)
            throw new InvalidOperationException(snapshot.Status.Problems.FirstOrDefault() ?? snapshot.Status.Detail);
        LocalRunnerProfile profile = snapshot.Profiles.SingleOrDefault(item => item.Profile.Id == profileId)?.Profile
            ?? throw new InvalidOperationException("The requested local instance is not configured.");
        _ = Process.Start(CreateRdpStartInfo(profile))
            ?? throw new InvalidOperationException("Windows did not start the local instance viewport.");
    }

    internal static ProcessStartInfo CreateRdpStartInfo(LocalRunnerProfile profile) => new(
        "mstsc.exe",
        $"\"{WriteRdpProfile(profile)}\"")
    {
        UseShellExecute = true,
    };

    internal static ProcessStartInfo CreateRdpStartInfo(LocalRunnerProfile profile, string rdpRoot) => new(
        "mstsc.exe",
        $"\"{WriteRdpProfile(profile, rdpRoot)}\"")
    {
        UseShellExecute = true,
    };

    internal static string CreateRdpProfileContent(LocalRunnerProfile profile)
    {
        ValidateRdpProfile(profile);
        return string.Join(Environment.NewLine,
        [
            "screen mode id:i:2",
            "use multimon:i:0",
            "session bpp:i:32",
            $"full address:s:{profile.LoopbackAddress}:{TermServiceConfigurationManager.LocalPort}",
            $"username:s:{Environment.MachineName}\\{profile.AccountName}",
            "authentication level:i:0",
            "enablecredsspsupport:i:1",
            "prompt for credentials:i:0",
            "redirectclipboard:i:0",
            "redirectprinters:i:0",
            "redirectcomports:i:0",
            "redirectsmartcards:i:0",
            "devicestoredirect:s:",
            "drivestoredirect:s:",
            "audiomode:i:2",
        ]) + Environment.NewLine;
    }

    private static string WriteRdpProfile(LocalRunnerProfile profile, string? rdpRoot = null)
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
            File.WriteAllText(temporary, CreateRdpProfileContent(profile), Encoding.Unicode);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return path;
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
        string helper = Path.Combine(AppContext.BaseDirectory, "LilacMacro.SessionSetup.exe");
        if (!File.Exists(helper)) throw new FileNotFoundException("The signed local-instance setup helper is missing.", helper);
        using Process process = Process.Start(new ProcessStartInfo(helper)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = string.Join(' ', arguments.Select(QuoteArgument)),
        }) ?? throw new InvalidOperationException("Windows did not start the local-instance setup helper.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        LocalInstanceManagerSnapshot snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(snapshot.Status.Problems.FirstOrDefault() ?? snapshot.Status.Detail);
        return snapshot;
    }

    private static string QuoteArgument(string value) => value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        ? value
        : $"\"{value.Replace("\"", "\\\"")}\"";

    private static IReadOnlyList<LocalRunnerProfile> ProfilesFrom(LocalSessionProvisioningManifest? manifest)
    {
        if (manifest is null) return [];
        if (manifest.RunnerProfiles.Count > 0) return manifest.RunnerProfiles;
        return [new LocalRunnerProfile
        {
            Id = "runner-1",
            DisplayName = "Runner 1",
            AccountName = manifest.RunnerAccountName,
            RunnerSid = manifest.RunnerSid,
            Slot = 1,
            LoopbackAddress = "127.0.0.2",
            ConfigurationMode = RunnerConfigurationMode.Shared,
        }];
    }

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
