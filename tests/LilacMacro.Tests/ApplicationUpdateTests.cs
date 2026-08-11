using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Updates;
using LilacMacro.Core.Updates;

namespace LilacMacro.Tests;

public sealed class ApplicationUpdateTests
{
    [Fact]
    public void Semantic_versions_require_exact_three_part_values()
    {
        Assert.True(LilacSemanticVersion.TryParseTag("v1.2.3", out LilacSemanticVersion version));
        Assert.Equal(new LilacSemanticVersion(1, 2, 3), version);
        Assert.False(LilacSemanticVersion.TryParseTag("1.2.3", out _));
        Assert.False(LilacSemanticVersion.TryParseTag("v1.2.3-beta", out _));
        Assert.False(LilacSemanticVersion.TryParseTag("v1.02.3", out _));
    }

    [Fact]
    public void Release_policy_accepts_only_a_newer_exact_four_asset_release()
    {
        VerifiedUpdateRelease? selected = GitHubReleasePolicy.Select(
            [Release("1.0.72"), Release("1.0.71")],
            new LilacSemanticVersion(1, 0, 70),
            includePrerelease: false);

        Assert.NotNull(selected);
        Assert.Equal(new LilacSemanticVersion(1, 0, 72), selected.Version);
        Assert.Equal(GitHubReleasePolicy.InstallerName, selected.Installer.Name);
    }

    [Fact]
    public void Stable_channel_ignores_prerelease_and_prerelease_channel_accepts_it()
    {
        GitHubReleaseCandidate preview = Release("1.0.72", prerelease: true);
        GitHubReleaseCandidate stable = Release("1.0.71");

        Assert.Equal(
            new LilacSemanticVersion(1, 0, 71),
            GitHubReleasePolicy.Select([preview, stable], new LilacSemanticVersion(1, 0, 70), false)!.Version);
        Assert.Equal(
            new LilacSemanticVersion(1, 0, 72),
            GitHubReleasePolicy.Select([preview, stable], new LilacSemanticVersion(1, 0, 70), true)!.Version);
    }

    [Fact]
    public void Selected_release_fails_closed_on_extra_or_untrusted_assets()
    {
        GitHubReleaseCandidate valid = Release("1.0.71");
        GitHubReleaseCandidate extra = valid with
        {
            Assets = [.. valid.Assets, Asset(valid.TagName, "unexpected.zip", 100)],
        };
        GitHubReleaseCandidate wrongUrl = valid with
        {
            Assets = valid.Assets.Select(asset => asset.Name == GitHubReleasePolicy.InstallerName
                ? asset with { DownloadUrl = "https://example.invalid/LilacMacro-Setup.exe" }
                : asset).ToArray(),
        };

        Assert.Throws<InvalidDataException>(() => GitHubReleasePolicy.Select(
            [extra], new LilacSemanticVersion(1, 0, 70), false));
        Assert.Throws<InvalidDataException>(() => GitHubReleasePolicy.Select(
            [wrongUrl], new LilacSemanticVersion(1, 0, 70), false));
    }

    [Fact]
    public void Checksum_manifest_requires_one_exact_installer_entry()
    {
        string digest = new('A', 64);
        Assert.Equal(digest, GitHubReleasePolicy.ParseInstallerChecksum($"{digest}  LilacMacro-Setup.exe\n"));
        Assert.Throws<InvalidDataException>(() => GitHubReleasePolicy.ParseInstallerChecksum(
            $"{digest} *LilacMacro-Setup.exe"));
    }

    [Fact]
    public void Coordinated_update_state_round_trips_bounded_participants()
    {
        CoordinatedUpdateState state = new(
            Guid.NewGuid(),
            new LilacSemanticVersion(1, 0, 71),
            new string('B', 64),
            @"C:\ProgramData\LilacMacro\Session\update-request.txt",
            [15, 8, 15],
            ["runner-2", "runner-1"]);

        CoordinatedUpdateState parsed = CoordinatedUpdateText.ParseState(
            CoordinatedUpdateText.SerializeState(state));

        Assert.Equal(state.OperationId, parsed.OperationId);
        Assert.Equal([8, 15], parsed.ParticipantProcessIds);
        Assert.Equal(["runner-1", "runner-2"], parsed.ActiveRunnerIds);
    }

    [Fact]
    public void Shutdown_request_requires_a_newer_version_and_fresh_time()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CoordinatedUpdateRequest request = new(Guid.NewGuid(), new LilacSemanticVersion(1, 0, 71), now);

        Assert.True(CoordinatedUpdateText.ShouldClose(request, new LilacSemanticVersion(1, 0, 70), now));
        Assert.False(CoordinatedUpdateText.ShouldClose(request, new LilacSemanticVersion(1, 0, 71), now));
        Assert.False(CoordinatedUpdateText.ShouldClose(request with { RequestedAtUtc = now.AddMinutes(-11) }, new LilacSemanticVersion(1, 0, 70), now));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/LeniLilac/LilacMacro/releases", false, true)]
    [InlineData("https://github.com/LeniLilac/LilacMacro/releases/download/v1.0.71/LilacMacro-Setup.exe", false, true)]
    [InlineData("https://release-assets.githubusercontent.com/object", true, true)]
    [InlineData("https://release-assets.githubusercontent.com/object", false, false)]
    [InlineData("http://github.com/LeniLilac/LilacMacro", false, false)]
    [InlineData("https://example.invalid/update", true, false)]
    public void Update_transport_bounds_hosts_and_redirects(string value, bool redirected, bool allowed)
    {
        if (allowed) UpdateHttpTransport.ValidateUri(new Uri(value), redirected);
        else Assert.Throws<InvalidDataException>(() => UpdateHttpTransport.ValidateUri(new Uri(value), redirected));
    }

    [Theory]
    [InlineData("https://www.roblox.com/download/client?os=win", false, true)]
    [InlineData("https://setup.rbxcdn.com/version-test-RobloxPlayerInstaller.exe", true, true)]
    [InlineData("https://setup.rbxcdn.com/version-test-RobloxPlayerInstaller.exe", false, false)]
    [InlineData("http://www.roblox.com/download", false, false)]
    public void Runner_bootstrap_accepts_only_the_official_https_redirect_chain(
        string value,
        bool redirected,
        bool allowed) =>
        Assert.Equal(allowed, RunnerFirstLaunchBootstrap.IsTrustedInstallerUri(new Uri(value), redirected));

    [Fact]
    public async Task Update_and_display_options_persist_and_compact_mode_forces_minimize()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-update-options-{Guid.NewGuid():N}");
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            first.SetUpdateOptions(checkOnStartup: false, includePrerelease: true);
            first.SetDisplayOptions(MacroLayoutProfile.Compact1366x768, MacroMinimizeBehavior.KeepVisible);
            await first.FlushAsync();

            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.False(restored.CheckForUpdatesOnStartup);
            Assert.True(restored.IncludePrereleaseUpdates);
            Assert.Equal(MacroLayoutProfile.Compact1366x768, restored.LayoutProfile);
            Assert.Equal(MacroMinimizeBehavior.WhileRunning, restored.EffectiveMinimizeBehavior);
            Assert.False(MacroDisplayPolicy.AllowsDock(restored.LayoutProfile));
            Assert.Equal((1366d, 768d), MacroDisplayPolicy.TargetSize(restored.LayoutProfile));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static GitHubReleaseCandidate Release(string version, bool prerelease = false)
    {
        string tag = $"v{version}";
        return new GitHubReleaseCandidate(
            tag,
            $"https://github.com/{GitHubReleasePolicy.Repository}/releases/tag/{tag}",
            Draft: false,
            Prerelease: prerelease,
            DateTimeOffset.UtcNow,
            [
                Asset(tag, GitHubReleasePolicy.InstallerName, 50 * 1024 * 1024),
                Asset(tag, GitHubReleasePolicy.ChecksumName, 96),
                Asset(tag, "LICENSE.md", 1000),
                Asset(tag, "NOTICE.md", 1000),
            ]);
    }

    private static GitHubReleaseAsset Asset(string tag, string name, long size) => new(
        name,
        size,
        $"https://github.com/{GitHubReleasePolicy.Repository}/releases/download/{tag}/{name}",
        $"sha256:{new string('a', 64)}");
}
