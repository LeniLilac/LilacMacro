using System.ComponentModel;
using System.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Updates;

namespace LilacMacro.App.Updates;

internal sealed class ApplicationUpdateService : IDisposable
{
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromMinutes(30);
    private readonly UpdateHttpTransport transport;
    private readonly GitHubUpdateClient client;
    private readonly UpdatePackageDownloader downloader;

    public ApplicationUpdateService()
    {
        transport = new UpdateHttpTransport();
        client = new GitHubUpdateClient(transport);
        downloader = new UpdatePackageDownloader(transport);
    }

    public LilacSemanticVersion CurrentVersion { get; } = LilacSemanticVersion.FromAssemblyVersion(
        typeof(ApplicationUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public VerifiedUpdateRelease? AvailableRelease { get; private set; }

    public bool CanInstall => !MacroInstanceContext.Current.IsManagedRunner && IsInstalledApplication();

    public async Task<VerifiedUpdateRelease?> CheckAsync(
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        AvailableRelease = await client.CheckAsync(CurrentVersion, includePrerelease, cancellationToken)
            .ConfigureAwait(false);
        return AvailableRelease;
    }

    public Task<IReadOnlyList<VerifiedUpdateRelease>> ListReleasesAsync(
        bool includePrerelease,
        CancellationToken cancellationToken = default) =>
        client.ListAsync(includePrerelease, cancellationToken);

    public async Task DownloadReleaseInstallerAsync(
        VerifiedUpdateRelease release,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (MacroInstanceContext.Current.IsManagedRunner)
            throw new InvalidOperationException("Download releases from This desktop.");
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string destination = Path.GetFullPath(destinationPath);
        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (destinationDirectory is null || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException("The selected download folder does not exist.");

        Guid operationId = Guid.NewGuid();
        string root = CoordinatedUpdateStateStore.CacheRoot(operationId);
        string temporary = destination + $".{operationId:N}.tmp";
        try
        {
            (string installerPath, string installerSha256) = await downloader.DownloadAsync(
                release,
                root,
                cancellationToken).ConfigureAwait(false);
            await UpdatePackageDownloader.VerifyBeforeLaunchAsync(
                installerPath,
                installerSha256,
                cancellationToken).ConfigureAwait(false);
            File.Copy(installerPath, temporary, overwrite: true);
            await UpdatePackageDownloader.VerifyBeforeLaunchAsync(
                temporary,
                installerSha256,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            TryDeleteDirectory(root);
        }
    }

    public async Task LaunchAvailableUpdateAsync(CancellationToken cancellationToken = default)
    {
        VerifiedUpdateRelease release = AvailableRelease
            ?? throw new InvalidOperationException("Check for an update before installing it.");
        if (!CanInstall)
            throw new InvalidOperationException(MacroInstanceContext.Current.IsManagedRunner
                ? "Install updates from This desktop."
                : "Coordinated updates require the installed Program Files build.");

        Guid operationId = Guid.NewGuid();
        string root = CoordinatedUpdateStateStore.CacheRoot(operationId);
        (string installerPath, string installerSha256) = await downloader.DownloadAsync(
            release,
            root,
            cancellationToken).ConfigureAwait(false);
        string statePath = await CoordinatedUpdateStateStore.WriteAsync(
            operationId,
            release,
            installerSha256,
            cancellationToken).ConfigureAwait(false);
        await UpdatePackageDownloader.VerifyBeforeLaunchAsync(
            installerPath,
            installerSha256,
            cancellationToken).ConfigureAwait(false);
        try
        {
            _ = Process.Start(CreateInstallerStartInfo(installerPath, statePath))
                ?? throw new InvalidOperationException("Windows did not start the verified update installer.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("The update UAC prompt was cancelled.", exception);
        }
    }

    internal static ProcessStartInfo CreateInstallerStartInfo(string installerPath, string statePath)
    {
        ProcessStartInfo startInfo = new(installerPath)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add($"/UPDATESTATE={statePath}");
        startInfo.ArgumentList.Add("/SILENT");
        startInfo.ArgumentList.Add("/NOCANCEL");
        startInfo.ArgumentList.Add("/NORESTART");
        startInfo.ArgumentList.Add("/SP-");
        return startInfo;
    }

    private static bool IsInstalledApplication()
    {
        string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            + Path.DirectorySeparatorChar;
        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        return baseDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(baseDirectory, "LilacMacro.SessionSetup.exe"));
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => transport.Dispose();
}
