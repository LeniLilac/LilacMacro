using System.Net;
using System.Text.Json;
using System.IO.Compression;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.Tests;

public sealed class PrivacyTelemetryTests
{
    [Fact]
    public async Task Privacy_choices_require_first_run_acceptance_and_persist_independently()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.False(first.HasAcceptedCurrentPrivacyChoices);
            Assert.True(first.OnlineFeaturesEnabled);
            Assert.True(first.TelemetryEnabled);
            Assert.True(first.AutomaticErrorReportsEnabled);

            await first.SavePrivacyChoicesAsync(
                onlineFeaturesEnabled: false,
                telemetryEnabled: true,
                automaticErrorReportsEnabled: true);

            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.True(restored.HasAcceptedCurrentPrivacyChoices);
            Assert.False(restored.OnlineFeaturesEnabled);
            Assert.True(restored.TelemetryEnabled);
            Assert.True(restored.AutomaticErrorReportsEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_privacy_save_revokes_opt_outs_and_does_not_activate_or_claim_acceptance()
    {
        string blockingFile = Path.Combine(
            Path.GetTempPath(),
            "LilacMacro.Tests",
            $"privacy-block-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.GetDirectoryName(blockingFile)!);
        await File.WriteAllTextAsync(blockingFile, "blocks settings directory");
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(blockingFile));

            await Assert.ThrowsAsync<IOException>(() => owner.SavePrivacyChoicesAsync(
                onlineFeaturesEnabled: false,
                telemetryEnabled: false,
                automaticErrorReportsEnabled: true));

            Assert.False(owner.OnlineFeaturesEnabled);
            Assert.False(owner.TelemetryEnabled);
            Assert.True(owner.AutomaticErrorReportsEnabled);
            Assert.False(owner.HasAcceptedCurrentPrivacyChoices);
        }
        finally
        {
            File.Delete(blockingFile);
        }
    }

    [Fact]
    public async Task Shared_store_opt_out_invalidates_other_owner_and_survives_stale_ordinary_save()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState seed = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await seed.SavePrivacyChoicesAsync(true, true, true);
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            MacroOwnerState stale = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));

            await first.SavePrivacyChoicesAsync(false, false, false);

            Assert.False(await stale.IsOnlineFeaturesDurablyEnabledAsync());
            Assert.False(await stale.IsTelemetryDurablyEnabledAsync());
            Assert.False(await stale.AreAutomaticReportsDurablyEnabledAsync());
            stale.SetUpdateOptions(checkOnStartup: false, includePrerelease: true);
            await stale.FlushAsync();
            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.False(restored.OnlineFeaturesEnabled);
            Assert.False(restored.TelemetryEnabled);
            Assert.False(restored.AutomaticErrorReportsEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_authoritative_privacy_record_fails_closed_after_opt_in()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(true, true, true);
            File.Delete(Path.Combine(root, "privacy-choices.json"));

            Assert.False(await owner.IsOnlineFeaturesDurablyEnabledAsync());
            Assert.False(await owner.IsTelemetryDurablyEnabledAsync());
            Assert.False(await owner.AreAutomaticReportsDurablyEnabledAsync());
            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.False(restored.HasAcceptedCurrentPrivacyChoices);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Shared_store_disable_then_reenable_does_not_release_old_telemetry_generation()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState seed = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await seed.SavePrivacyChoicesAsync(true, true, false);
            MacroOwnerState changer = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            MacroOwnerState stale = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            DeepDebugSessionService deepDebug = new(root);
            RecordingProductTelemetryTransport transport = new();
            await using ProductTelemetryService telemetry = new(
                deepDebug,
                stale,
                new DiagnosticInstallationStore(root),
                transport,
                TimeSpan.FromMilliseconds(150));
            telemetry.Start();

            await changer.SavePrivacyChoicesAsync(true, false, false);
            await changer.SavePrivacyChoicesAsync(true, true, false);
            await Task.Delay(250);

            Assert.Empty(transport.Batches);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrent_partial_privacy_edits_merge_without_resurrecting_stale_choice()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState seed = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await seed.SavePrivacyChoicesAsync(true, true, true);
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            MacroOwnerState stale = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));

            await first.SavePrivacyChoiceAsync(PrivacyChoiceKind.AutomaticErrorReports, false);
            await stale.SavePrivacyChoiceAsync(PrivacyChoiceKind.Telemetry, false);

            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.True(restored.OnlineFeaturesEnabled);
            Assert.False(restored.TelemetryEnabled);
            Assert.False(restored.AutomaticErrorReportsEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Shared_store_opt_out_prevents_sibling_deep_debug_upload()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState seed = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await seed.SavePrivacyChoicesAsync(true, false, true);
            MacroOwnerState changer = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            MacroOwnerState stale = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            DeepDebugSessionService deepDebug = NewDeepDebug(root);
            RecordingUploadTransport uploads = new();
            await using AutomaticDiagnosticReportService reports = new(
                deepDebug,
                stale,
                new DiagnosticInstallationStore(root),
                uploads);

            await changer.SavePrivacyChoiceAsync(PrivacyChoiceKind.AutomaticErrorReports, false);
            DeepDebugScope? scope = await deepDebug.OpenSessionAsync(
                "shared opt out",
                new DeepDebugOperationContext("test"));
            deepDebug.RecordEvent("macro", "runtime_error");
            await scope!.CompleteAsync("error");
            await Task.Delay(100);

            Assert.Null(uploads.ArchivePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Telemetry_transport_posts_only_bounded_fixed_schema_to_exact_endpoint()
    {
        RecordingHandler handler = new(request => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            RequestMessage = request,
        });
        using HttpClient client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using ProductTelemetryTransport transport = new(client, ownsClient: false);
        ProductTelemetryBatch batch = CreateBatch();

        await transport.SendAsync(batch);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(ProductTelemetryPolicy.Endpoint, request.RequestUri);
        Assert.Equal(HttpMethod.Post, request.Method);
        using JsonDocument body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal(batch.InstallId, body.RootElement.GetProperty("installId").GetGuid());
        Assert.Equal("session-started", body.RootElement.GetProperty("events")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Telemetry_transport_rejects_redirect_and_policy_rejects_free_form_values()
    {
        RecordingHandler handler = new(request => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            RequestMessage = request,
            Headers = { Location = new Uri("https://example.com/collect") },
        });
        using HttpClient client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using ProductTelemetryTransport transport = new(client, ownsClient: false);

        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(CreateBatch()));
        ProductTelemetryBatch unsafeBatch = CreateBatch() with
        {
            Events = [new ProductTelemetryEvent(
                ProductTelemetryKind.OperationError,
                DateTimeOffset.UtcNow,
                Feature: "C:\\Users\\name\\secret.txt")],
        };
        Assert.Throws<InvalidDataException>(() => ProductTelemetryPolicy.Validate(unsafeBatch));
    }

    [Fact]
    public async Task Telemetry_drops_old_generation_across_disable_and_reenable()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(true, true, false);
            DeepDebugSessionService deepDebug = new(root);
            RecordingProductTelemetryTransport transport = new();
            await using ProductTelemetryService telemetry = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                transport,
                TimeSpan.FromMilliseconds(150));
            telemetry.Start();

            await owner.SavePrivacyChoicesAsync(true, false, false);
            await owner.SavePrivacyChoicesAsync(true, true, false);
            await Task.Delay(250);

            Assert.Empty(transport.Batches);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Telemetry_preserves_gpu_zero_capability()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(true, true, false);
            DeepDebugSessionService deepDebug = new(root);
            RecordingProductTelemetryTransport transport = new();
            await using ProductTelemetryService telemetry = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                transport,
                TimeSpan.FromMilliseconds(10));
            telemetry.Start();
            deepDebug.RecordEvent("ocr", "inference_completed", new
            {
                InferenceMilliseconds = 18,
                Device = "gpu:0",
            });

            await transport.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            ProductTelemetryEvent timing = Assert.Single(
                transport.Batches.SelectMany(batch => batch.Events),
                item => item.Kind == ProductTelemetryKind.OcrTiming);
            Assert.Equal("gpu:0", timing.GraphicsCapability);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Ocr_setup_failure_telemetry_is_bounded_and_rate_limited_per_version_and_device()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(true, true, false);
            DeepDebugSessionService deepDebug = new(root);
            RecordingProductTelemetryTransport transport = new();
            ProductTelemetryService telemetry = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                transport,
                TimeSpan.FromMilliseconds(10));
            telemetry.Start();

            deepDebug.RecordEvent("ocr_setup", "setup_failed", new
            {
                Device = "gpu:0",
                FailureCode = "winget_unavailable",
                SetupStage = "python-bootstrap",
                DurationMilliseconds = 42,
                ProcessExitCode = 7,
                PythonLauncherPresent = true,
                WingetPresent = false,
                ExistingOcrPythonPresent = false,
                RuntimeMarkerPresent = false,
                Error = "C:\\Users\\name\\secret.txt",
            });

            await Task.Delay(25);
            await transport.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            await telemetry.DisposeAsync();

            ProductTelemetryEvent setup = Assert.Single(
                transport.Batches.SelectMany(batch => batch.Events),
                item => item.Kind == ProductTelemetryKind.OcrSetupFailure);
            Assert.Equal("ocr-setup", setup.Feature);
            Assert.Equal("winget_unavailable", setup.Outcome);
            Assert.Equal("python-bootstrap", setup.SetupStage);
            Assert.Equal("gpu:0", setup.RequestedDevice);
            Assert.Equal(42, setup.DurationMilliseconds);
            Assert.Equal(7, setup.ProcessExitCode);
            Assert.False(setup.WingetPresent);
            Assert.DoesNotContain("secret", JsonSerializer.Serialize(setup), StringComparison.OrdinalIgnoreCase);
            ProductTelemetryPolicy.Validate(new ProductTelemetryBatch(
                Guid.NewGuid(), "1.2.3", 1, [setup]));

            DeepDebugSessionService secondDeepDebug = new(root);
            RecordingProductTelemetryTransport secondTransport = new();
            await using ProductTelemetryService secondTelemetry = new(
                secondDeepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                secondTransport,
                TimeSpan.FromMilliseconds(10));
            secondTelemetry.Start();
            secondDeepDebug.RecordEvent("ocr_setup", "setup_failed", new
            {
                Device = "gpu:0",
                FailureCode = "winget_unavailable",
                SetupStage = "python-bootstrap",
                DurationMilliseconds = 43,
                ProcessExitCode = 7,
                PythonLauncherPresent = true,
                WingetPresent = false,
                ExistingOcrPythonPresent = false,
                RuntimeMarkerPresent = false,
            });
            await Task.Delay(250);

            Assert.DoesNotContain(
                secondTransport.Batches.SelectMany(batch => batch.Events),
                item => item.Kind == ProductTelemetryKind.OcrSetupFailure);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Ocr_setup_failure_does_not_trigger_an_automatic_report()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(false, false, true);
            DeepDebugSessionService deepDebug = new(root);
            RecordingUploadTransport uploads = new();
            await using AutomaticDiagnosticReportService reports = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                uploads);

            deepDebug.RecordEvent("ocr_setup", "setup_failed", new
            {
                Device = "cpu",
                FailureCode = "winget_unavailable",
                SetupStage = "python-bootstrap",
                DurationMilliseconds = 42,
                PythonLauncherPresent = true,
                WingetPresent = false,
                ExistingOcrPythonPresent = false,
                RuntimeMarkerPresent = false,
            });
            await Task.Delay(150);

            Assert.Null(uploads.ArchivePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Observation_stream_remains_available_when_deep_debug_is_off()
    {
        string root = NewTemporaryDirectory();
        try
        {
            DeepDebugSessionService deepDebug = new(root);
            List<DeepDebugObservation> observations = [];
            deepDebug.ObservationRecorded += (_, observation) => observations.Add(observation);
            deepDebug.FrameRecorded += (_, observation) => observations.Add(observation);

            deepDebug.RecordEvent("macro", "runtime_error", new { Error = "secret" });
            deepDebug.RecordPng([1, 2, 3], "important-frame");

            Assert.False(deepDebug.IsActive);
            Assert.Collection(
                observations,
                item => Assert.Equal("runtime_error", item.Action),
                item => Assert.Equal([1, 2, 3], item.PngBytes));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Failing_optional_observer_does_not_block_other_observers_or_automation()
    {
        string root = NewTemporaryDirectory();
        try
        {
            DeepDebugSessionService deepDebug = new(root);
            List<DeepDebugObservation> observations = [];
            deepDebug.ObservationRecorded += (_, _) => throw new InvalidOperationException("observer failed");
            deepDebug.ObservationRecorded += (_, observation) => observations.Add(observation);

            deepDebug.RecordEvent("macro", "runtime_error");

            Assert.Equal("runtime_error", Assert.Single(observations).Action);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Automatic_error_report_uploads_only_completed_deep_debug_archive()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(
                onlineFeaturesEnabled: false,
                telemetryEnabled: false,
                automaticErrorReportsEnabled: true);
            DeepDebugSessionService deepDebug = NewDeepDebug(root);
            RecordingUploadTransport uploads = new();
            AutomaticDiagnosticReportService reports = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                uploads);

            DeepDebugScope? scope = await deepDebug.OpenSessionAsync(
                "automatic error",
                new DeepDebugOperationContext("test"));
            deepDebug.RecordEvent("macro", "runtime_error", new { Error = "C:\\Users\\name" });
            await scope!.CompleteAsync("error");
            await uploads.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(DiagnosticArchiveKind.DeepDebug, uploads.Kind);
            Assert.NotNull(uploads.ArchivePath);
            Assert.DoesNotContain(Environment.UserName, uploads.TextEntries, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("manifest.json", uploads.EntryNames);
            Assert.Contains("events.jsonl", uploads.EntryNames);
            await reports.DisposeAsync();
            Assert.True(File.Exists(uploads.ArchivePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Automatic_error_uploads_pause_with_deep_debug_and_ignore_clean_archives()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(false, false, true);
            DeepDebugSessionService deepDebug = NewDeepDebug(root);
            RecordingUploadTransport uploads = new();
            await using AutomaticDiagnosticReportService reports = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                uploads);

            await deepDebug.UpdateOptionsAsync(enabled: false);
            deepDebug.RecordEvent("macro", "runtime_error");
            await Task.Delay(100);
            Assert.Null(uploads.ArchivePath);

            await deepDebug.UpdateOptionsAsync(enabled: true);
            DeepDebugScope? scope = await deepDebug.OpenSessionAsync(
                "clean run",
                new DeepDebugOperationContext("test"));
            await scope!.CompleteAsync("success");
            await Task.Delay(100);

            Assert.Null(uploads.ArchivePath);
            Assert.True(File.Exists(deepDebug.LastArchivePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Automatic_report_service_preserves_existing_archives_and_legacy_markers()
    {
        string settingsRoot = NewTemporaryDirectory();
        DeepDebugSessionService deepDebug = new(settingsRoot);
        Directory.CreateDirectory(deepDebug.DiagnosticsRoot);
        string archive = Path.Combine(deepDebug.DiagnosticsRoot, "deep-debug-test-20000101-000000-id.zip");
        string marker = archive + ".uploaded-delete-pending";
        string unrelated = Path.Combine(deepDebug.DiagnosticsRoot, "owner-kept.zip");
        await File.WriteAllTextAsync(archive, "sent");
        await File.WriteAllTextAsync(marker, string.Empty);
        await File.WriteAllTextAsync(unrelated, "keep");
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(settingsRoot));
            await using AutomaticDiagnosticReportService reports = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(settingsRoot),
                new RecordingUploadTransport());

            Assert.True(File.Exists(archive));
            Assert.True(File.Exists(marker));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(settingsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Automatic_reports_do_not_cross_disable_and_reenable_generation()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            await owner.SavePrivacyChoicesAsync(true, false, true);
            DeepDebugSessionService deepDebug = NewDeepDebug(root);
            BlockingUploadTransport uploads = new();
            await using AutomaticDiagnosticReportService reports = new(
                deepDebug,
                owner,
                new DiagnosticInstallationStore(root),
                uploads);

            DeepDebugScope? scope = await deepDebug.OpenSessionAsync(
                "privacy generation",
                new DeepDebugOperationContext("test"));
            deepDebug.RecordEvent("macro", "runtime_error");
            await scope!.CompleteAsync("error");
            await uploads.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await owner.SavePrivacyChoicesAsync(true, false, false);
            await owner.SavePrivacyChoicesAsync(true, false, true);
            await Task.Delay(100);

            Assert.Equal(1, uploads.CallCount);
            Assert.True(uploads.FirstCancelled.Task.IsCompletedSuccessfully);
            Assert.True(File.Exists(deepDebug.LastArchivePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DeepDebugSessionService NewDeepDebug(string root) => new(
        root,
        diagnosticsRoot: null,
        _ => 300 * DiagnosticUploadPolicy.OneGiB);

    private static ProductTelemetryBatch CreateBatch() => new(
        Guid.NewGuid(),
        "1.2.3",
        1,
        [new ProductTelemetryEvent(
            ProductTelemetryKind.SessionStarted,
            DateTimeOffset.UtcNow,
            Feature: "macro",
            Outcome: "started",
            OperatingSystem: "windows-11.0",
            LogicalProcessorCount: 16,
            GraphicsCapability: "not-observed")]);

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return response(request);
        }
    }

    private sealed class RecordingUploadTransport : IDiagnosticUploadTransport
    {
        public TaskCompletionSource Completed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string? ArchivePath { get; private set; }

        public DiagnosticArchiveKind? Kind { get; private set; }

        public string TextEntries { get; private set; } = string.Empty;

        public List<string> EntryNames { get; } = [];

        public async Task<DiagnosticUploadResult> UploadAsync(
            string archivePath,
            DiagnosticArchiveKind kind,
            string appVersion,
            Guid installId,
            string operatingSystemVersion,
            IProgress<DiagnosticUploadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArchivePath = archivePath;
            Kind = kind;
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                EntryNames.Add(entry.FullName);
                if (!entry.FullName.EndsWith(".json", StringComparison.Ordinal)
                    && !entry.FullName.EndsWith(".jsonl", StringComparison.Ordinal)) continue;
                using StreamReader reader = new(entry.Open());
                TextEntries += await reader.ReadToEndAsync(cancellationToken);
            }
            Completed.TrySetResult();
            return new DiagnosticUploadResult(
                Guid.NewGuid(),
                "Verifying",
                DateTimeOffset.UtcNow.AddHours(1));
        }
    }

    private sealed class RecordingProductTelemetryTransport : IProductTelemetryTransport
    {
        public List<ProductTelemetryBatch> Batches { get; } = [];

        public TaskCompletionSource Completed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(
            ProductTelemetryBatch batch,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(batch);
            Completed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingUploadTransport : IDiagnosticUploadTransport
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public TaskCompletionSource FirstStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstCancelled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DiagnosticUploadResult> UploadAsync(
            string archivePath,
            DiagnosticArchiveKind kind,
            string appVersion,
            Guid installId,
            string operatingSystemVersion,
            IProgress<DiagnosticUploadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            FirstStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking transport unexpectedly resumed.");
            }
            catch (OperationCanceledException)
            {
                FirstCancelled.TrySetResult();
                throw;
            }
        }
    }
}
