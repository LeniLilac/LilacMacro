namespace LilacMacro.Core.Updates;

public sealed record GitHubReleaseAsset(
    string Name,
    long Size,
    string DownloadUrl,
    string Digest);

public sealed record GitHubReleaseCandidate(
    string TagName,
    string ReleaseUrl,
    bool Draft,
    bool Prerelease,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<GitHubReleaseAsset> Assets);

public sealed record VerifiedUpdateRelease(
    LilacSemanticVersion Version,
    string TagName,
    string ReleaseUrl,
    bool Prerelease,
    DateTimeOffset PublishedAtUtc,
    GitHubReleaseAsset Installer,
    GitHubReleaseAsset ChecksumManifest);

public static class GitHubReleasePolicy
{
    public const string Repository = "LeniLilac/LilacMacro";
    public const string InstallerName = "LilacMacro-Setup.exe";
    public const string ChecksumName = "LilacMacro-Setup.exe.sha256";
    public static readonly IReadOnlySet<string> RequiredAssetNames = new HashSet<string>(StringComparer.Ordinal)
    {
        InstallerName,
        ChecksumName,
        "LICENSE.md",
        "NOTICE.md",
    };

    public static VerifiedUpdateRelease? Select(
        IReadOnlyList<GitHubReleaseCandidate> releases,
        LilacSemanticVersion currentVersion,
        bool includePrerelease)
    {
        ArgumentNullException.ThrowIfNull(releases);
        GitHubReleaseCandidate? selected = releases
            .Where(release => !release.Draft && (includePrerelease || !release.Prerelease))
            .Select(release => (Release: release, Valid: LilacSemanticVersion.TryParseTag(release.TagName, out LilacSemanticVersion version), Version: version))
            .Where(item => item.Valid && item.Version.CompareTo(currentVersion) > 0)
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.Release.PublishedAtUtc)
            .Select(item => item.Release)
            .FirstOrDefault();
        if (selected is null) return null;
        if (!LilacSemanticVersion.TryParseTag(selected.TagName, out LilacSemanticVersion selectedVersion))
            throw new InvalidDataException("The selected release tag is not an exact semantic version.");
        ValidateRelease(selected, selectedVersion);
        GitHubReleaseAsset installer = selected.Assets.Single(asset => asset.Name == InstallerName);
        GitHubReleaseAsset checksums = selected.Assets.Single(asset => asset.Name == ChecksumName);
        return new VerifiedUpdateRelease(
            selectedVersion,
            selected.TagName,
            selected.ReleaseUrl,
            selected.Prerelease,
            selected.PublishedAtUtc,
            installer,
            checksums);
    }

    public static string ParseInstallerChecksum(string manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string normalized = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        string suffix = $"  {InstallerName}";
        if (normalized.Length != 64 + suffix.Length || !normalized.EndsWith(suffix, StringComparison.Ordinal))
            throw new InvalidDataException("The release checksum manifest has an unexpected format.");
        string digest = normalized[..64];
        if (!IsSha256(digest)) throw new InvalidDataException("The release checksum is invalid.");
        return digest.ToUpperInvariant();
    }

    public static string ParseAssetDigest(string digest)
    {
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !IsSha256(digest[prefix.Length..]))
        {
            throw new InvalidDataException("A release asset is missing its GitHub SHA-256 digest.");
        }
        return digest[prefix.Length..].ToUpperInvariant();
    }

    private static void ValidateRelease(GitHubReleaseCandidate release, LilacSemanticVersion version)
    {
        string expectedReleaseUrl = $"https://github.com/{Repository}/releases/tag/v{version}";
        if (!string.Equals(release.ReleaseUrl, expectedReleaseUrl, StringComparison.Ordinal))
            throw new InvalidDataException("The release URL does not match its semantic tag.");
        if (release.Assets.Count != RequiredAssetNames.Count
            || !release.Assets.Select(asset => asset.Name).ToHashSet(StringComparer.Ordinal).SetEquals(RequiredAssetNames))
        {
            throw new InvalidDataException("The release does not contain the exact four-asset inventory.");
        }
        if (release.Assets.GroupBy(asset => asset.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("The release contains duplicate asset names.");
        foreach (GitHubReleaseAsset asset in release.Assets)
        {
            _ = ParseAssetDigest(asset.Digest);
            ValidateAssetSize(asset);
            string expected = $"https://github.com/{Repository}/releases/download/{release.TagName}/{asset.Name}";
            if (!string.Equals(asset.DownloadUrl, expected, StringComparison.Ordinal))
                throw new InvalidDataException($"The direct URL for {asset.Name} is not trusted.");
        }
    }

    private static void ValidateAssetSize(GitHubReleaseAsset asset)
    {
        long maximum = asset.Name switch
        {
            InstallerName => 512L * 1024 * 1024,
            ChecksumName => 256,
            _ => 1024 * 1024,
        };
        long minimum = asset.Name == InstallerName ? 1024 * 1024 : 1;
        if (asset.Size < minimum || asset.Size > maximum)
            throw new InvalidDataException($"The declared size for {asset.Name} is outside the trusted bounds.");
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
