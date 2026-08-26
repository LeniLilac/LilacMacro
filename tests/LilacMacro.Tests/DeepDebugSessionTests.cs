using System.IO.Compression;
using System.Text.Json;
using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Services;

namespace LilacMacro.Tests;

public sealed class DeepDebugSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LilacMacro.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(120, true)]
    public void Periodic_capture_failures_are_coalesced(int failures, bool expected) =>
        Assert.Equal(expected, DeepDebugCaptureFailurePolicy.ShouldReport(failures));

    [Fact]
    public void New_install_defaults_enable_fixed_interval_and_capacity_tier()
    {
        DeepDebugSessionService service = NewService(freeGiB: 75);

        Assert.True(service.Options.Enabled);
        Assert.Equal(1_000, DeepDebugOptions.CaptureIntervalMilliseconds);
        Assert.Equal(10, service.Options.MaximumArchiveStorageGiB);
        Assert.False(service.IsTemporarilyPausedByStorage);
    }

    [Theory]
    [InlineData(2, 3, true)]
    [InlineData(10, 3, false)]
    [InlineData(51, 10, false)]
    [InlineData(201, 30, false)]
    public void Storage_policy_uses_free_space_tiers(
        int freeGiB,
        int expectedStorageGiB,
        bool paused)
    {
        long freeBytes = freeGiB * DiagnosticUploadPolicy.OneGiB;

        Assert.Equal(expectedStorageGiB, DeepDebugStoragePolicy.RecommendedStorageGiB(freeBytes));
        Assert.Equal(
            paused,
            DeepDebugStoragePolicy.Evaluate(expectedStorageGiB, freeBytes, 0).CapturePaused);
    }

    [Fact]
    public async Task Low_disk_temporarily_pauses_without_turning_logging_off()
    {
        long freeBytes = 2 * DiagnosticUploadPolicy.OneGiB;
        DeepDebugSessionService service = NewService(availableFreeBytes: _ => freeBytes);

        DeepDebugScope? paused = await service.OpenSessionAsync(
            "low disk",
            new DeepDebugOperationContext("test"));
        Assert.Null(paused);
        Assert.True(service.Options.Enabled);
        Assert.True(service.IsTemporarilyPausedByStorage);

        freeBytes = 20 * DiagnosticUploadPolicy.OneGiB;
        DeepDebugScope? resumed = await service.OpenSessionAsync(
            "space restored",
            new DeepDebugOperationContext("test"));
        Assert.NotNull(resumed);
        await resumed!.CompleteAsync("success");
    }

    [Fact]
    public async Task Configured_storage_is_lowered_to_available_archive_pool()
    {
        DeepDebugSessionService service = NewService(freeGiB: 7);

        await service.UpdateOptionsAsync(maximumArchiveStorageGiB: 30);
        DeepDebugSessionService restored = NewService(freeGiB: 7);

        Assert.Equal(7, service.Options.MaximumArchiveStorageGiB);
        Assert.Equal(7, restored.Options.MaximumArchiveStorageGiB);
    }

    [Fact]
    public async Task Session_writes_redacted_agent_readable_archive_and_transition_frame()
    {
        const string operatingSystem = "Microsoft Windows NT 10.0.19045.6456";
        DeepDebugSessionService service = NewService(operatingSystemVersion: operatingSystem);
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "wire test",
            new DeepDebugOperationContext(
                "dataset-builder",
                new { Secret = "https://discord.com" + "/api/webhooks/" + "123/abc" }));

        service.RecordEvent("ocr", "state_evaluated", new
        {
            Path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "dataset"),
            Text = "Lobby",
        });
        service.RecordPng(TestPng(255, 0, 0), "live-client");
        await scope!.CompleteAsync("success");

        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("manifest.json", entries);
        Assert.Contains("events.jsonl", entries);
        Assert.Contains("timeline.md", entries);
        Assert.Contains("README.md", entries);
        Assert.Contains("frames/index.json", entries);
        Assert.Contains(entries, entry => entry.StartsWith("frames/frame-", StringComparison.Ordinal));
        string events = await ReadAsync(archive, "events.jsonl");
        Assert.DoesNotContain(Environment.UserName, events, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discord.com/api/webhooks", events, StringComparison.OrdinalIgnoreCase);
        string? retainedArtifact = null;
        foreach (string line in events.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("artifact", out JsonElement artifact))
                retainedArtifact = artifact.GetString();
        }
        Assert.NotNull(retainedArtifact);
        Assert.NotNull(archive.GetEntry(retainedArtifact!));
        string manifest = await ReadAsync(archive, "manifest.json");
        Assert.Contains("\"formatVersion\": 3", manifest, StringComparison.Ordinal);
        Assert.Contains("\"transitionFrames\": 1", manifest, StringComparison.Ordinal);
        Assert.Contains("\"validation\"", await ReadAsync(archive, "frames/index.json"), StringComparison.Ordinal);
        Assert.Contains(
            $"\"operatingSystem\": \"{operatingSystem}\"",
            await ReadAsync(archive, "configuration/environment.json"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_log_is_preserved_without_unrelated_frames()
    {
        DeepDebugSessionService service = NewService();
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "macro runtime",
            new DeepDebugOperationContext("main-macro"));
        const string message = "MATCH RUNTIME | Upgrade target did not produce physical selection proof.";

        service.RecordRuntimeLog(message);
        await scope!.CompleteAsync("stopped");

        Assert.True(service.LastArchivePath is not null, ReadFinalizationFailures(service.DiagnosticsRoot));
        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        string events = await ReadAsync(archive, "events.jsonl");
        Assert.Contains(message, events, StringComparison.Ordinal);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fixed_one_second_capture_records_periodic_observations()
    {
        DeepDebugSessionService service = NewService();
        using IDisposable registration = service.RegisterFrameCaptureProvider(
            "test",
            token =>
            {
                service.RecordPng(TestPng(1, 2, 3), "live-client");
                return Task.CompletedTask;
            });
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "periodic capture",
            new DeepDebugOperationContext("test"));

        await Task.Delay(2_250);
        await scope!.CompleteAsync("success");

        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        string events = await ReadAsync(archive, "events.jsonl");
        Assert.True(events.Split("\"action\":\"live-client\"", StringSplitOptions.None).Length >= 3);
        string configuration = await ReadAsync(archive, "configuration/deep-debug-options.json");
        Assert.DoesNotContain("captureInterval", configuration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evidence_retains_ten_seconds_before_and_after_error()
    {
        string evidenceRoot = Path.Combine(_root, "evidence-window");
        Directory.CreateDirectory(evidenceRoot);
        DateTimeOffset errorAt = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        RecordFrame(retention, evidenceRoot, errorAt.AddSeconds(-11), "before-11");
        RecordFrame(retention, evidenceRoot, errorAt.AddSeconds(-10), "before-10");
        retention.ObserveEvent("macro", "runtime_recovery", new { FailedTask = "tower" }, errorAt);
        RecordFrame(retention, evidenceRoot, errorAt, "at-error");
        RecordFrame(retention, evidenceRoot, errorAt.AddSeconds(10), "after-10");
        RecordFrame(retention, evidenceRoot, errorAt.AddSeconds(11), "after-11");
        long windowBytes = new[] { "before-10", "at-error", "after-10" }
            .Sum(name => new FileInfo(Path.Combine(evidenceRoot, name + ".png")).Length);

        retention.Complete(windowBytes);

        Assert.False(File.Exists(Path.Combine(evidenceRoot, "before-11.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "before-10.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "at-error.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "after-10.png")));
        Assert.False(File.Exists(Path.Combine(evidenceRoot, "after-11.png")));
        Assert.Equal(1, retention.WindowCount);
    }

    [Fact]
    public void Evidence_keeps_complete_frame_stream_below_archive_pressure()
    {
        string evidenceRoot = Path.Combine(_root, "evidence-complete");
        Directory.CreateDirectory(evidenceRoot);
        DateTimeOffset started = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        RecordFrame(retention, evidenceRoot, started, "first");
        RecordFrame(retention, evidenceRoot, started.AddMinutes(20), "second");
        long retainedBytes = retention.RetainedBytes;

        retention.Complete(retainedBytes);

        Assert.False(retention.IsOptimized);
        Assert.Equal(2, retention.RetainedFrameCount);
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "first.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "second.png")));
    }

    [Fact]
    public void Evidence_uses_freed_capacity_only_after_archive_pressure()
    {
        string evidenceRoot = Path.Combine(_root, "evidence-pressure");
        Directory.CreateDirectory(evidenceRoot);
        DateTimeOffset started = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        RecordFrame(retention, evidenceRoot, started, "old");
        RecordFrame(retention, evidenceRoot, started.AddSeconds(11), "recent");
        long belowPressureBytes = retention.RetainedBytes;

        retention.OptimizeWhenAbove(belowPressureBytes + 1);
        Assert.False(retention.IsOptimized);
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "old.png")));

        RecordFrame(retention, evidenceRoot, started.AddSeconds(12), "crossing");
        retention.OptimizeWhenAbove(belowPressureBytes);

        Assert.True(retention.IsOptimized);
        Assert.Equal(2, retention.RetainedFrameCount);
        Assert.True(retention.RetainedBytes <= belowPressureBytes);
        Assert.False(File.Exists(Path.Combine(evidenceRoot, "old.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "recent.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "crossing.png")));
    }

    [Fact]
    public async Task Evidence_converts_old_ordinary_frames_to_jpeg_and_keeps_recent_png()
    {
        string evidenceRoot = Path.Combine(_root, "evidence-jpeg");
        Directory.CreateDirectory(evidenceRoot);
        DateTimeOffset started = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        RecordFrame(retention, evidenceRoot, started, "old");
        RecordFrame(retention, evidenceRoot, started.AddSeconds(11), "recent");

        await retention.CompleteAsync(new FakeFrameCodec(success: true), long.MaxValue);

        Assert.True(File.Exists(Path.Combine(evidenceRoot, "old.jpeg")));
        Assert.False(File.Exists(Path.Combine(evidenceRoot, "old.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "recent.png")));
        Assert.Equal(1, retention.JpegFrameCount);
        Assert.Equal(0, retention.AvifFrameCount);
        DeepDebugEvidenceFrame encoded = Assert.Single(retention.Frames, frame => frame.Format == "jpeg");
        Assert.Equal("decode-verified", encoded.Validation);
        Assert.Equal(14, encoded.Quality);
    }

    [Fact]
    public async Task Evidence_keeps_png_when_jpeg_validation_fails()
    {
        string evidenceRoot = Path.Combine(_root, "evidence-jpeg-failure");
        Directory.CreateDirectory(evidenceRoot);
        DateTimeOffset started = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        RecordFrame(retention, evidenceRoot, started, "old");
        RecordFrame(retention, evidenceRoot, started.AddSeconds(11), "recent");

        await retention.CompleteAsync(new FakeFrameCodec(success: false), long.MaxValue);

        Assert.True(File.Exists(Path.Combine(evidenceRoot, "old.png")));
        Assert.Equal(0, retention.JpegFrameCount);
        Assert.Equal(0, retention.AvifFrameCount);
        Assert.Contains(retention.Frames, frame => frame.Validation == "decode-failed");
    }

    [Fact]
    public async Task Production_codec_writes_decode_verified_quality_14_jpeg_without_external_tools()
    {
        string evidenceRoot = Path.Combine(_root, "jpeg-codec");
        Directory.CreateDirectory(evidenceRoot);
        string pngPath = Path.Combine(evidenceRoot, "source.png");
        await File.WriteAllBytesAsync(pngPath, NoisyPng(42));
        DeepDebugFrameCodec codec = new(evidenceRoot);

        DeepDebugFrameEncodingResult result = await codec.EncodeAsync(
            pngPath,
            lossless: false,
            waitForLease: false);

        Assert.True(result.Success, result.Validation);
        Assert.Equal("jpeg", result.Format);
        Assert.Equal(14, result.Quality);
        Assert.Equal([0xff, 0xd8], result.Bytes![..2]);
        Assert.True(result.Bytes.Length < new FileInfo(pngPath).Length);
    }

    [Fact]
    public void Frame_artifact_paths_are_rewritten_in_one_pass()
    {
        Dictionary<string, string> replacements = new(StringComparer.Ordinal)
        {
            ["frames/frame-000000001-live-client.png"] =
                "frames/frame-000000001-live-client.avif",
            ["frames/frame-000000002-unit-control-region.png"] =
                "frames/frame-000000002-unit-control-region.avif",
        };
        string line =
            "frames/frame-000000001-live-client.png " +
            "[frame](frames/frame-000000001-live-client.png) " +
            "frames/frame-000000002-unit-control-region.png " +
            "frames/unrelated.png";

        string rewritten = DeepDebugFrameArtifactIndex.RewriteLine(line, replacements);

        Assert.Equal(
            "frames/frame-000000001-live-client.avif " +
            "[frame](frames/frame-000000001-live-client.avif) " +
            "frames/frame-000000002-unit-control-region.avif " +
            "frames/unrelated.png",
            rewritten);
    }

    [Fact]
    public async Task Frame_artifact_index_replaces_opened_log_files_after_streaming()
    {
        string staging = Path.Combine(_root, "artifact-rewrite");
        string framesRoot = Path.Combine(staging, "frames");
        Directory.CreateDirectory(framesRoot);
        string original = Path.Combine(framesRoot, "frame-000000001-live-client.png");
        await File.WriteAllBytesAsync(original, [1, 2, 3]);
        DeepDebugEvidenceFrame frame = new(
            original,
            DateTimeOffset.UtcNow,
            3,
            0,
            fullClient: true)
        {
            ArtifactPath = "frames/frame-000000001-live-client.avif",
            Format = "avif",
        };
        await File.WriteAllTextAsync(
            Path.Combine(staging, "events.jsonl"),
            "{\"artifactPath\":\"frames/frame-000000001-live-client.png\"}\n");
        await File.WriteAllTextAsync(
            Path.Combine(staging, "timeline.md"),
            "[frame](frames/frame-000000001-live-client.png)\n");

        await DeepDebugFrameArtifactIndex.RewriteAsync(
            staging,
            [frame],
            new JsonSerializerOptions());

        Assert.Contains(".avif", await File.ReadAllTextAsync(Path.Combine(staging, "events.jsonl")));
        Assert.Contains(".avif", await File.ReadAllTextAsync(Path.Combine(staging, "timeline.md")));
        Assert.False(File.Exists(Path.Combine(staging, "events.jsonl.rewrite")));
    }

    [Fact]
    public void Overlapping_error_windows_merge_and_terminal_evidence_has_priority()
    {
        string evidenceRoot = Path.Combine(_root, "evidence-priority");
        Directory.CreateDirectory(evidenceRoot);
        DateTimeOffset started = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        retention.ObserveEvent("macro", "runtime_recovery", new { FailedTask = "one" }, started);
        RecordFrame(retention, evidenceRoot, started, "recoverable");
        retention.ObserveEvent("input", "click_failed", new { Operation = "two" }, started.AddSeconds(5));
        RecordFrame(retention, evidenceRoot, started.AddSeconds(5), "overlap");
        retention.ObserveEvent("macro", "runtime_error", new { Error = "fatal" }, started.AddSeconds(30));
        RecordFrame(retention, evidenceRoot, started.AddSeconds(30), "terminal");

        long terminalBytes = new FileInfo(Path.Combine(evidenceRoot, "terminal.png")).Length;
        retention.Complete(terminalBytes);

        Assert.Equal(2, retention.WindowCount);
        Assert.False(File.Exists(Path.Combine(evidenceRoot, "recoverable.png")));
        Assert.False(File.Exists(Path.Combine(evidenceRoot, "overlap.png")));
        Assert.True(File.Exists(Path.Combine(evidenceRoot, "terminal.png")));
        Assert.Equal(1, retention.DiscardedWindowCount);
    }

    [Theory]
    [InlineData("application", "unhandled_exception", true)]
    [InlineData("macro", "runtime_error", true)]
    [InlineData("macro", "runtime_recovery", false)]
    [InlineData("ocr_setup", "setup_failed", false)]
    [InlineData("ocr", "inference_failed", false)]
    [InlineData("ocr", "worker_timeout", false)]
    [InlineData("local_instance", "operation_failed", false)]
    [InlineData("window", "capture_exhausted", false)]
    [InlineData("route_optimizer_test", "trial_failed", false)]
    public void Evidence_policy_classifies_actionable_failures(
        string category,
        string action,
        bool terminal)
    {
        bool classified = DeepDebugEvidencePolicy.TryClassifyError(
            category,
            action,
            new { FailureCode = "bounded_failure", Stage = "test" },
            DateTimeOffset.UtcNow,
            out DeepDebugErrorMarker? marker);

        Assert.True(classified);
        Assert.Equal(
            terminal ? DeepDebugErrorSeverity.Terminal : DeepDebugErrorSeverity.Recoverable,
            marker!.Severity);
    }

    [Fact]
    public void Evidence_policy_does_not_treat_periodic_capture_gaps_as_errors()
    {
        bool classified = DeepDebugEvidencePolicy.TryClassifyError(
            "diagnostic",
            "periodic_live_frame_capture_failed",
            new { Error = "Roblox is unavailable while restart is in progress." },
            DateTimeOffset.UtcNow,
            out DeepDebugErrorMarker? marker);

        Assert.False(classified);
        Assert.Null(marker);
    }

    [Fact]
    public async Task Completion_gate_allows_one_finalizer_and_releases_other_callers()
    {
        DeepDebugCompletionGate gate = new();
        bool[] owners = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(gate.TryOwn)));

        Assert.Single(owners, owner => owner);
        Task<Exception?> waiter = gate.WaitAsync();
        Assert.False(waiter.IsCompleted);
        gate.Finish(null);
        Assert.Null(await waiter);
    }

    [Fact]
    public async Task Crash_and_scope_completion_share_one_archive_finalizer()
    {
        DeepDebugSessionService service = NewService();
        TaskCompletionSource captureStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseCapture = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable registration = service.RegisterFrameCaptureProvider(
            "completion-race",
            async _ =>
            {
                captureStarted.TrySetResult();
                await releaseCapture.Task;
            });
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "completion race",
            new DeepDebugOperationContext("completion-race"));
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Task ordinary = scope!.CompleteAsync("stopped");
        Task crash = service.CompleteActiveAsync("unhandled-error", new InvalidOperationException("test"));
        releaseCapture.TrySetResult();
        await Task.WhenAll(ordinary, crash);

        Assert.Single(Directory.EnumerateFiles(service.DiagnosticsRoot, "deep-debug-*.zip"));
        Assert.Empty(Directory.EnumerateFiles(
            service.DiagnosticsRoot,
            "finalization-error.txt",
            SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(service.DiagnosticsRoot, ".deep-debug-*"));
    }

    [Fact]
    public void Perceptual_hash_is_stable_for_same_pixels()
    {
        byte[] first = PatternPng(leftBright: true);
        byte[] same = PatternPng(leftBright: true);
        byte[] different = PatternPng(leftBright: false);

        ulong firstHash = DeepDebugPerceptualHash.Create(first);

        Assert.Equal(firstHash, DeepDebugPerceptualHash.Create(same));
        Assert.NotEqual(firstHash, DeepDebugPerceptualHash.Create(different));
    }

    [Fact]
    public async Task Disabled_logging_does_not_create_session()
    {
        DeepDebugSessionService service = NewService();
        await service.UpdateOptionsAsync(enabled: false);

        DeepDebugScope? scope = await service.OpenSessionAsync(
            "disabled",
            new DeepDebugOperationContext("test"));

        Assert.Null(scope);
        Assert.False(Directory.Exists(service.DiagnosticsRoot));
    }

    [Fact]
    public async Task Concurrent_session_is_rejected()
    {
        DeepDebugSessionService service = NewService();
        DeepDebugScope? first = await service.OpenSessionAsync(
            "first",
            new DeepDebugOperationContext("test"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenSessionAsync(
            "second",
            new DeepDebugOperationContext("test")));
        await first!.CompleteAsync("success");
    }

    [Fact]
    public async Task Session_includes_only_registered_visual_profile_revision()
    {
        string profileRoot = Path.Combine(_root, "profiles", "wire-test");
        string revision = Path.Combine(profileRoot, "revisions", "20260808T010203000Z-test");
        Directory.CreateDirectory(revision);
        await File.WriteAllTextAsync(
            Path.Combine(revision, "profile.json"),
            $$"""{"source":"C:\\Users\\{{Environment.UserName}}\\profile"}""");
        await File.WriteAllBytesAsync(Path.Combine(revision, "median.pgm"), [80, 53, 10]);
        string locator = Path.Combine(profileRoot, "locator.json");
        await File.WriteAllTextAsync(locator, "{\"bounds\":[1,2,3,4]}");
        DeepDebugSessionService service = NewService();
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "profile snapshot",
            new DeepDebugOperationContext("test"));

        service.RecordVisualProfileRevision("wire-test", revision, locator);
        await scope!.CompleteAsync("success");

        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        string prefix = "visual-profiles/wire-test/20260808T010203000Z-test/";
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "profile.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "median.pgm");
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "locator.json");
        string copied = await ReadAsync(archive, prefix + "profile.json");
        Assert.DoesNotContain(Environment.UserName, copied, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"visualProfiles\": 1", await ReadAsync(archive, "manifest.json"));
    }

    [Fact]
    public async Task Missing_registered_locator_is_reported_in_manifest()
    {
        string revision = Path.Combine(_root, "profiles", "wire-test", "revisions", "revision");
        Directory.CreateDirectory(revision);
        await File.WriteAllTextAsync(Path.Combine(revision, "profile.json"), "{}");
        DeepDebugSessionService service = NewService();
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "missing locator",
            new DeepDebugOperationContext("test"));

        service.RecordVisualProfileRevision(
            "wire-test",
            revision,
            Path.Combine(_root, "profiles", "wire-test", "locator.json"));
        await scope!.CompleteAsync("success");

        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        Assert.Contains(
            "Visual locator was unavailable for profile wire-test",
            await ReadAsync(archive, "manifest.json"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_diagnostics_use_one_storage_budget_across_profiles()
    {
        string sharedDiagnostics = Path.Combine(_root, "machine-diagnostics");
        Func<string, long> capacity = _ => 100 * DiagnosticUploadPolicy.OneGiB;
        DeepDebugSessionService owner = new(
            Path.Combine(_root, "owner-profile"),
            sharedDiagnostics,
            capacity);
        DeepDebugSessionService runner = new(
            Path.Combine(_root, "runner-profile"),
            sharedDiagnostics,
            capacity);

        await owner.UpdateOptionsAsync(enabled: false, maximumArchiveStorageGiB: 30);
        runner.RefreshOptions();

        Assert.Equal(30, runner.Options.MaximumArchiveStorageGiB);
        Assert.True(runner.Options.Enabled);
        Assert.False(owner.Options.Enabled);
        Assert.True(File.Exists(Path.Combine(sharedDiagnostics, "deep-debug-retention.json")));
    }

    [Fact]
    public async Task Shared_storage_pruning_deletes_oldest_archives_by_total_bytes()
    {
        string sharedDiagnostics = Path.Combine(_root, "byte-pruning");
        Directory.CreateDirectory(sharedDiagnostics);
        for (int index = 0; index < 3; index++)
        {
            string path = Path.Combine(sharedDiagnostics, $"deep-debug-test-{index}.zip");
            await File.WriteAllBytesAsync(path, new byte[6]);
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(index));
        }

        DeepDebugConfigurationStore.PruneArchivesWithinBudget(sharedDiagnostics, maximumBytes: 10);

        string retained = Assert.Single(Directory.EnumerateFiles(sharedDiagnostics, "deep-debug-*.zip"));
        Assert.EndsWith("deep-debug-test-2.zip", retained, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Archive_stays_below_injected_hard_limit()
    {
        DeepDebugArchiveLimits limits = new(
            64 * 1024,
            1 * 1024 * 1024,
            16 * 1024,
            8 * 1024,
            8 * 1024,
            4 * 1024);
        DeepDebugSessionService service = NewService(limits: limits);
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "bounded",
            new DeepDebugOperationContext("test"));
        service.RecordEvent("macro", "runtime_error", new { Error = "failure" });
        for (int index = 0; index < 4; index++)
            service.RecordPng(NoisyPng(index), "live-client");

        await scope!.CompleteAsync("error");

        Assert.True(new FileInfo(service.LastArchivePath!).Length <= limits.MaximumArchiveBytes);
    }

    private DeepDebugSessionService NewService(
        int freeGiB = 300,
        Func<string, long>? availableFreeBytes = null,
        DeepDebugArchiveLimits? limits = null,
        IDeepDebugFrameCodec? frameCodec = null,
        string? operatingSystemVersion = null) => new(
            _root,
            diagnosticsRoot: null,
            availableFreeBytes ?? (_ => freeGiB * DiagnosticUploadPolicy.OneGiB),
            limits,
            frameCodec: frameCodec,
            operatingSystemVersion: operatingSystemVersion);

    private static byte[] TestPng(byte red, byte green, byte blue) =>
        PngEncoder.Encode(new RgbImage(2, 2,
        [
            red, green, blue,
            blue, red, green,
            green, blue, red,
            red, blue, green,
        ], takeOwnership: true));

    private static byte[] PatternPng(bool leftBright)
    {
        byte[] pixels = new byte[8 * 8 * 3];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool bright = x < 4 == leftBright;
                byte value = bright ? (byte)240 : (byte)10;
                int offset = (y * 8 + x) * 3;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }
        return PngEncoder.Encode(new RgbImage(8, 8, pixels, takeOwnership: true));
    }

    private static byte[] NoisyPng(int seed)
    {
        byte[] pixels = new byte[128 * 128 * 3];
        new Random(seed).NextBytes(pixels);
        return PngEncoder.Encode(new RgbImage(128, 128, pixels, takeOwnership: true));
    }

    private static void RecordFrame(
        DeepDebugEvidenceRetention retention,
        string root,
        DateTimeOffset timestamp,
        string name)
    {
        byte[] png = TestPng((byte)name.Length, 20, 30);
        string path = Path.Combine(root, name + ".png");
        File.WriteAllBytes(path, png);
        retention.RecordFrame(path, timestamp, png, fullClient: true);
    }

    private static async Task<string> ReadAsync(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = Assert.Single(archive.Entries, candidate => candidate.FullName == name);
        await using Stream stream = entry.Open();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }

    private static string ReadFinalizationFailures(string diagnosticsRoot) => string.Join(
        Environment.NewLine,
        Directory.Exists(diagnosticsRoot)
            ? Directory.EnumerateFiles(diagnosticsRoot, "finalization-error.txt", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
            : ["Diagnostics directory was not created."]);

    private sealed class FakeFrameCodec(bool success) : IDeepDebugFrameCodec
    {
        public Task<DeepDebugFrameEncodingResult> EncodeAsync(
            string pngPath,
            bool lossless,
            bool waitForLease,
            CancellationToken cancellationToken = default) => Task.FromResult(success
                ? new DeepDebugFrameEncodingResult(
                    true,
                    [1],
                    lossless ? "pixel-exact" : "decode-verified",
                    lossless ? "avif" : "jpeg",
                    lossless ? null : 14)
                : new DeepDebugFrameEncodingResult(
                    false,
                    null,
                    "decode-failed",
                    lossless ? "avif" : "jpeg",
                    lossless ? null : 14));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The test runner can release a ZIP handle after disposal returns.
        }
    }
}
