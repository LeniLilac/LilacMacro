using System.Text.Json;
using System.Text.Json.Serialization;
using LilacMacro.Core.Updates;

namespace LilacMacro.App.Updates;

internal sealed class GitHubUpdateClient(UpdateHttpTransport transport)
{
    private static readonly Uri ReleasesUri = new(
        $"https://api.github.com/repos/{GitHubReleasePolicy.Repository}/releases?per_page=100");

    public async Task<VerifiedUpdateRelease?> CheckAsync(
        LilacSemanticVersion currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        GitHubReleaseCandidate[] candidates = await FetchAsync(cancellationToken).ConfigureAwait(false);
        return GitHubReleasePolicy.Select(candidates, currentVersion, includePrerelease);
    }

    public async Task<IReadOnlyList<VerifiedUpdateRelease>> ListAsync(
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        GitHubReleaseCandidate[] candidates = await FetchAsync(cancellationToken).ConfigureAwait(false);
        return GitHubReleasePolicy.ListDownloadable(candidates, includePrerelease);
    }

    private async Task<GitHubReleaseCandidate[]> FetchAsync(CancellationToken cancellationToken)
    {
        byte[] payload = await transport.GetBytesAsync(ReleasesUri, 4 * 1024 * 1024, cancellationToken)
            .ConfigureAwait(false);
        List<ReleaseDto>? releases = JsonSerializer.Deserialize<List<ReleaseDto>>(payload);
        return releases?.Select(ToCandidate).ToArray()
            ?? throw new InvalidDataException("GitHub returned an invalid release response.");
    }

    private static GitHubReleaseCandidate ToCandidate(ReleaseDto release) => new(
        release.TagName ?? string.Empty,
        release.HtmlUrl ?? string.Empty,
        release.Draft,
        release.Prerelease,
        release.PublishedAt ?? DateTimeOffset.MinValue,
        (release.Assets ?? []).Select(asset => new GitHubReleaseAsset(
            asset.Name ?? string.Empty,
            asset.Size,
            asset.BrowserDownloadUrl ?? string.Empty,
            asset.Digest ?? string.Empty)).ToArray());

    private sealed record ReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
        [JsonPropertyName("draft")]
        public bool Draft { get; init; }
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }
        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }
        [JsonPropertyName("assets")]
        public List<AssetDto>? Assets { get; init; }
    }

    private sealed record AssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
        [JsonPropertyName("size")]
        public long Size { get; init; }
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
