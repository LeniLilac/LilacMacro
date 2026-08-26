using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Updates;
using LilacMacro.Core.Updates;
using LilacMacro.Windows.LocalSession;
using System.Diagnostics;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.X509;
using System.Text;

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
    public void Release_policy_accepts_only_a_newer_exact_six_asset_release()
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
    public void Download_catalog_includes_current_and_older_official_versions_in_descending_order()
    {
        GitHubReleaseCandidate preview = Release("1.0.72", prerelease: true);
        GitHubReleaseCandidate current = Release("1.0.71");
        GitHubReleaseCandidate older = Release("1.0.69");
        GitHubReleaseCandidate unverifiable = Release("1.0.68") with { Assets = [] };

        IReadOnlyList<VerifiedUpdateRelease> stable = GitHubReleasePolicy.ListDownloadable(
            [older, preview, unverifiable, current],
            includePrerelease: false);
        IReadOnlyList<VerifiedUpdateRelease> all = GitHubReleasePolicy.ListDownloadable(
            [older, preview, unverifiable, current],
            includePrerelease: true);

        Assert.Equal(
            [new LilacSemanticVersion(1, 0, 71), new LilacSemanticVersion(1, 0, 69)],
            stable.Select(release => release.Version));
        Assert.Equal(
            [new LilacSemanticVersion(1, 0, 72), new LilacSemanticVersion(1, 0, 71), new LilacSemanticVersion(1, 0, 69)],
            all.Select(release => release.Version));
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
    public void Project_release_signature_binds_tag_size_and_installer_digest()
    {
        Ed25519PrivateKeyParameters privateKey = new(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(), 0);
        string publicKey = Convert.ToBase64String(
            SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(privateKey.GeneratePublicKey()).GetDerEncoded());
        VerifiedUpdateRelease release = GitHubReleasePolicy.Select(
            [Release("1.0.72")],
            new LilacSemanticVersion(1, 0, 71),
            includePrerelease: false)!;
        string digest = new('A', 64);
        byte[] manifest = Encoding.UTF8.GetBytes(
            $"{{\"format\":\"lilacmacro.release\",\"schemaVersion\":1,\"keyId\":\"test-key\",\"algorithm\":\"Ed25519\",\"tag\":\"{release.TagName}\",\"sourceCommit\":\"{new string('a', 40)}\",\"installer\":{{\"name\":\"LilacMacro-Setup.exe\",\"size\":{release.Installer.Size},\"sha256\":\"{digest}\"}}}}");
        Ed25519Signer signer = new();
        signer.Init(true, privateKey);
        signer.BlockUpdate(manifest, 0, manifest.Length);
        string signature = Convert.ToBase64String(signer.GenerateSignature());
        ReleaseManifestVerifier verifier = new("test-key", publicKey);

        Assert.Equal(digest, verifier.Verify(manifest, signature, release));
        manifest[^2] ^= 1;
        Assert.Throws<InvalidDataException>(() => verifier.Verify(manifest, signature, release));
    }

    [Fact]
    public void Coordinated_update_state_round_trips_bounded_participants()
    {
        CoordinatedUpdateState state = new(
            Guid.NewGuid(),
            new LilacSemanticVersion(1, 0, 71),
            new string('B', 64),
            @"C:\ProgramData\LilacMacro\UpdateControl\update-request.txt",
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

    [Fact]
    public void In_app_update_launches_the_verified_installer_elevated_and_unattended()
    {
        ProcessStartInfo start = ApplicationUpdateService.CreateInstallerStartInfo(
            @"C:\Users\owner\AppData\Local\LilacMacro\updates\operation\LilacMacro-Setup.exe",
            @"C:\Users\owner\AppData\Local\LilacMacro\updates\operation\update-state.txt");

        Assert.True(start.UseShellExecute);
        Assert.Equal("runas", start.Verb);
        Assert.Equal(
            [
                @"/UPDATESTATE=C:\Users\owner\AppData\Local\LilacMacro\updates\operation\update-state.txt",
                "/SILENT",
                "/NOCANCEL",
                "/NORESTART",
                "/SP-",
            ],
            start.ArgumentList);
    }

    [Theory]
    [InlineData(RunnerSessionConnectionState.Active, 2, true)]
    [InlineData(RunnerSessionConnectionState.Disconnected, 3, true)]
    [InlineData(RunnerSessionConnectionState.SignedOut, null, false)]
    [InlineData(RunnerSessionConnectionState.Unknown, null, false)]
    public void Coordinated_update_relaunches_every_logged_in_runner_session(
        RunnerSessionConnectionState state,
        int? sessionId,
        bool expected)
    {
        Assert.Equal(
            expected,
            CoordinatedUpdateStateStore.ShouldRelaunchRunner(new RunnerSessionObservation(state, sessionId)));
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

    [Theory]
    [InlineData((int)MacroMinimizeBehavior.KeepVisible)]
    [InlineData((int)MacroMinimizeBehavior.WhileRunning)]
    [InlineData((int)MacroMinimizeBehavior.OnApplicationStart)]
    public async Task Update_and_display_options_persist_and_compact_mode_restores_full_dock_preference(
        int configuredBehaviorValue)
    {
        MacroMinimizeBehavior configuredBehavior = (MacroMinimizeBehavior)configuredBehaviorValue;
        string root = Path.Combine(Path.GetTempPath(), $"lilac-update-options-{Guid.NewGuid():N}");
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            first.SetUpdateOptions(checkOnStartup: false, includePrerelease: true);
            first.MarkUpdateNotificationShown("1.2.3");
            first.SetDisplayOptions(MacroLayoutProfile.Full1920x1080, configuredBehavior);
            MacroMinimizeBehavior compactMinimize = MacroDisplayPolicy.ConfiguredMinimizeBehaviorForSelection(
                first.LayoutProfile,
                MacroLayoutProfile.Compact1366x768,
                MacroMinimizeBehavior.WhileRunning,
                first.MinimizeBehavior);
            first.SetDisplayOptions(MacroLayoutProfile.Compact1366x768, compactMinimize);
            first.SetRunnerLayoutProfile("runner-1", MacroLayoutProfile.Compact1366x768);
            await first.FlushAsync();

            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.False(restored.CheckForUpdatesOnStartup);
            Assert.True(restored.IncludePrereleaseUpdates);
            Assert.True(restored.WasUpdateNotificationShown("1.2.3"));
            Assert.False(restored.WasUpdateNotificationShown("1.2.4"));
            Assert.Equal(TimeSpan.FromMinutes(30), ApplicationUpdateService.AutomaticCheckInterval);
            Assert.Equal(MacroLayoutProfile.Compact1366x768, restored.LayoutProfile);
            Assert.Equal(configuredBehavior, restored.MinimizeBehavior);
            Assert.Equal(MacroMinimizeBehavior.WhileRunning, restored.EffectiveMinimizeBehavior);
            Assert.False(MacroDisplayPolicy.AllowsDock(restored.LayoutProfile));
            Assert.Equal((1366d, 768d), MacroDisplayPolicy.TargetSize(restored.LayoutProfile));
            Assert.Equal(MacroLayoutProfile.Compact1366x768, restored.RunnerLayoutProfile("runner-1"));
            Assert.Equal(MacroLayoutProfile.Full1920x1080, restored.RunnerLayoutProfile("runner-2"));
            Assert.Equal(MacroLayoutProfile.Compact1366x768, MacroDisplayPolicy.ManagedViewportLayout(1366, 768));
            Assert.Equal(MacroLayoutProfile.Full1920x1080, MacroDisplayPolicy.ManagedViewportLayout(1920, 1080));

            MacroMinimizeBehavior fullMinimize = MacroDisplayPolicy.ConfiguredMinimizeBehaviorForSelection(
                restored.LayoutProfile,
                MacroLayoutProfile.Full1920x1080,
                MacroMinimizeBehavior.WhileRunning,
                restored.MinimizeBehavior);
            restored.SetDisplayOptions(MacroLayoutProfile.Full1920x1080, fullMinimize);
            Assert.Equal(MacroLayoutProfile.Full1920x1080, restored.LayoutProfile);
            Assert.Equal(configuredBehavior, restored.MinimizeBehavior);
            Assert.Equal(configuredBehavior, restored.EffectiveMinimizeBehavior);
            await restored.FlushAsync();
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
                Asset(tag, GitHubReleasePolicy.ReleaseManifestName, 240),
                Asset(tag, GitHubReleasePolicy.ReleaseSignatureName, 89),
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
