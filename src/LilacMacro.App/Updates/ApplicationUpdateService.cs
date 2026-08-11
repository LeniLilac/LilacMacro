using System.ComponentModel;
using System.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Updates;

namespace LilacMacro.App.Updates;

internal sealed class ApplicationUpdateService : IDisposable
{
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
            _ = Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"/UPDATESTATE=\"{statePath}\"",
            }) ?? throw new InvalidOperationException("Windows did not start the verified update installer.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("The update UAC prompt was cancelled.", exception);
        }
    }

    private static bool IsInstalledApplication()
    {
        string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            + Path.DirectorySeparatorChar;
        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        return baseDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(baseDirectory, "LilacMacro.SessionSetup.exe"));
    }

    public void Dispose() => transport.Dispose();
}
