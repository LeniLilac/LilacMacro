using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using LilacMacro.Windows;

namespace LilacMacro.App.Infrastructure;

internal sealed class RunnerFirstLaunchBootstrap
{
    private static readonly Uri LoginUri = new("https://www.roblox.com/Login");
    private static readonly Uri InstallerUri = new("https://www.roblox.com/download/client?os=win");
    private readonly string localRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilacMacro");

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        string marker = Path.Combine(localRoot, "runner-first-launch-v1.complete");
        if (File.Exists(marker)) return;
        Directory.CreateDirectory(localRoot);
        await using FileStream? owner = TryAcquire(Path.Combine(localRoot, "runner-first-launch-v1.lock"));
        if (owner is null || File.Exists(marker)) return;

        _ = Process.Start(new ProcessStartInfo(LoginUri.AbsoluteUri) { UseShellExecute = true })
            ?? throw new InvalidOperationException("The Roblox login page could not be opened.");
        if (!IsRobloxInstalled()) await InstallRobloxAsync(cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O"), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    internal static bool IsTrustedInstallerUri(Uri uri, bool redirected) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.IdnHost.Equals("www.roblox.com", StringComparison.OrdinalIgnoreCase)
            || redirected && uri.IdnHost.Equals("setup.rbxcdn.com", StringComparison.OrdinalIgnoreCase));

    private async Task InstallRobloxAsync(CancellationToken cancellationToken)
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "LilacMacro", "runner-bootstrap");
        Directory.CreateDirectory(temporaryRoot);
        string installer = Path.Combine(temporaryRoot, $"RobloxPlayerInstaller-{Guid.NewGuid():N}.exe");
        try
        {
            await DownloadInstallerAsync(installer, cancellationToken).ConfigureAwait(false);
            AuthenticodeSignatureVerifier.VerifyTrusted(installer);
            using Process process = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true })
                ?? throw new InvalidOperationException("The Roblox installer could not be started.");
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException($"The Roblox installer exited with code {process.ExitCode}.");
            for (int attempt = 0; attempt < 60 && !IsRobloxInstalled(); attempt++)
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            if (!IsRobloxInstalled()) throw new InvalidOperationException("Roblox installation could not be verified.");
        }
        finally
        {
            if (File.Exists(installer)) File.Delete(installer);
        }
    }

    private static async Task DownloadInstallerAsync(string destination, CancellationToken cancellationToken)
    {
        using HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(3),
        };
        Uri current = InstallerUri;
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            if (!IsTrustedInstallerUri(current, redirect > 0))
                throw new InvalidDataException("The Roblox installer redirect is not trusted.");
            using HttpResponseMessage response = await client.GetAsync(
                current,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (IsRedirect(response.StatusCode))
            {
                Uri? location = response.Headers.Location;
                if (redirect == 3 || location is null)
                    throw new HttpRequestException("The Roblox installer redirect was invalid.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            const long maximumBytes = 64L * 1024 * 1024;
            if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
                throw new InvalidDataException("The Roblox installer is larger than the trusted bound.");
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream target = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            byte[] buffer = new byte[128 * 1024];
            long total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes) throw new InvalidDataException("The Roblox installer exceeded the trusted bound.");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            if (total < 1024 * 1024) throw new InvalidDataException("The Roblox installer response was unexpectedly small.");
            return;
        }
        throw new HttpRequestException("The Roblox installer could not be downloaded.");
    }

    private static bool IsRobloxInstalled()
    {
        string versions = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox",
            "Versions");
        return Directory.Exists(versions)
            && Directory.EnumerateFiles(versions, "RobloxPlayerBeta.exe", SearchOption.AllDirectories).Any();
    }

    private static FileStream? TryAcquire(string path)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
