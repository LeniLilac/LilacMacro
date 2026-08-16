using System.Security.Cryptography;
using System.Text;
using LilacMacro.Core.Updates;

namespace LilacMacro.App.Updates;

internal sealed class UpdatePackageDownloader(UpdateHttpTransport transport)
{
    private readonly ReleaseManifestVerifier _releaseVerifier = new();

    public async Task<(string InstallerPath, string Sha256)> DownloadAsync(
        VerifiedUpdateRelease release,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationRoot);
        string checksumPath = Path.Combine(destinationRoot, GitHubReleasePolicy.ChecksumName);
        string manifestPath = Path.Combine(destinationRoot, GitHubReleasePolicy.ReleaseManifestName);
        string signaturePath = Path.Combine(destinationRoot, GitHubReleasePolicy.ReleaseSignatureName);
        string installerPath = Path.Combine(destinationRoot, GitHubReleasePolicy.InstallerName);
        DeleteIfPresent(checksumPath);
        DeleteIfPresent(manifestPath);
        DeleteIfPresent(signaturePath);
        DeleteIfPresent(installerPath);
        try
        {
            string manifestAssetHash = await transport.DownloadAsync(
                new Uri(release.ReleaseManifest.DownloadUrl),
                manifestPath,
                release.ReleaseManifest.Size,
                cancellationToken).ConfigureAwait(false);
            RequireDigest(release.ReleaseManifest, manifestAssetHash);
            string signatureAssetHash = await transport.DownloadAsync(
                new Uri(release.ReleaseSignature.DownloadUrl),
                signaturePath,
                release.ReleaseSignature.Size,
                cancellationToken).ConfigureAwait(false);
            RequireDigest(release.ReleaseSignature, signatureAssetHash);
            string signedInstallerHash = _releaseVerifier.Verify(
                await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false),
                await File.ReadAllTextAsync(signaturePath, Encoding.ASCII, cancellationToken).ConfigureAwait(false),
                release);

            string checksumAssetHash = await transport.DownloadAsync(
                new Uri(release.ChecksumManifest.DownloadUrl),
                checksumPath,
                release.ChecksumManifest.Size,
                cancellationToken).ConfigureAwait(false);
            RequireDigest(release.ChecksumManifest, checksumAssetHash);
            string declaredInstallerHash = GitHubReleasePolicy.ParseInstallerChecksum(
                await File.ReadAllTextAsync(checksumPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false));

            string installerHash = await transport.DownloadAsync(
                new Uri(release.Installer.DownloadUrl),
                installerPath,
                release.Installer.Size,
                cancellationToken).ConfigureAwait(false);
            RequireDigest(release.Installer, installerHash);
            if (!string.Equals(installerHash, declaredInstallerHash, StringComparison.Ordinal))
                throw new InvalidDataException("The installer digest does not match the release checksum manifest.");
            if (!string.Equals(installerHash, signedInstallerHash, StringComparison.Ordinal))
                throw new InvalidDataException("The installer digest does not match the project-signed release manifest.");
            return (installerPath, installerHash);
        }
        catch
        {
            DeleteIfPresent(installerPath);
            throw;
        }
    }

    public static async Task VerifyBeforeLaunchAsync(
        string installerPath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            installerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Convert.ToHexString(hash), expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The cached update installer changed before launch.");
    }

    private static void RequireDigest(GitHubReleaseAsset asset, string actual)
    {
        string expected = GitHubReleasePolicy.ParseAssetDigest(asset.Digest);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"The GitHub digest for {asset.Name} did not match the downloaded file.");
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
