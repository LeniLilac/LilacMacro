using System.Text.Json;
using System.Threading.Channels;
using System.Net.Http;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.App.Diagnostics;

internal sealed class ProductTelemetryService : IAsyncDisposable
{
    private static readonly TimeSpan BatchWindow = TimeSpan.FromSeconds(10);
    private sealed record Consent(long Generation, CancellationToken Token);
    private sealed record QueuedEvent(ProductTelemetryEvent Event, Consent Consent);
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly DiagnosticInstallationStore _installation;
    private readonly ProductTelemetryRateLimitStore _rateLimits;
    private readonly IProductTelemetryTransport _transport;
    private readonly ProductTelemetryDeviceContext _deviceContext;
    private readonly Channel<QueuedEvent> _events = Channel.CreateBounded<QueuedEvent>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _choiceGate = new();
    private readonly List<CancellationTokenSource> _retiredChoiceCancellations = [];
    private CancellationTokenSource _choiceCancellation = new();
    private readonly TimeSpan _batchWindow;
    private long _choiceGeneration;
    private Task? _worker;

    internal ProductTelemetryService(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState,
        DiagnosticInstallationStore installation,
        IProductTelemetryTransport transport,
        TimeSpan? batchWindow = null,
        ProductTelemetryDeviceContext? deviceContext = null)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _installation = installation;
        _rateLimits = new ProductTelemetryRateLimitStore(installation.ConfigurationRoot);
        _transport = transport;
        _deviceContext = deviceContext ?? ProductTelemetryDeviceContext.Unknown;
        _batchWindow = batchWindow ?? BatchWindow;
        _deepDebug.ObservationRecorded += DeepDebug_OnObservationRecorded;
        _ownerState.PrivacyOptionsChanged += OwnerState_OnPrivacyOptionsChanged;
    }

    internal void Start()
    {
        if (_worker is not null) return;
        Queue(new ProductTelemetryEvent(
            ProductTelemetryKind.SessionStarted,
            DateTimeOffset.UtcNow,
            Feature: "macro",
            Outcome: "started",
            OperatingSystem: OperatingSystemValue(),
            LogicalProcessorCount: Environment.ProcessorCount,
            GraphicsCapability: "not-observed"));
        _worker = RunAsync(_cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _deepDebug.ObservationRecorded -= DeepDebug_OnObservationRecorded;
        _ownerState.PrivacyOptionsChanged -= OwnerState_OnPrivacyOptionsChanged;
        _events.Writer.TryComplete();
        _cancellation.Cancel();
        if (_worker is not null)
        {
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        }
        lock (_choiceGate)
        {
            _choiceCancellation.Cancel();
            _choiceCancellation.Dispose();
            foreach (CancellationTokenSource retired in _retiredChoiceCancellations) retired.Dispose();
            _retiredChoiceCancellations.Clear();
        }
        _cancellation.Dispose();
    }

    private void DeepDebug_OnObservationRecorded(object? sender, DeepDebugObservation observation)
    {
        ProductTelemetryEvent? item = Map(observation, _deviceContext);
        if (item is not null) Queue(item);
    }

    private void Queue(ProductTelemetryEvent item)
    {
        lock (_choiceGate)
        {
            if (!_ownerState.HasAcceptedCurrentPrivacyChoices || !_ownerState.TelemetryEnabled
                || !_ownerState.IsTelemetryDurablyEnabled())
                return;
            _events.Writer.TryWrite(new QueuedEvent(
                item,
                new Consent(_choiceGeneration, _choiceCancellation.Token)));
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await _rateLimits.LoadAsync(cancellationToken).ConfigureAwait(false);
        string appVersion = BuildVersion();
        QueuedEvent? pending = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (pending is null)
            {
                if (!await _events.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    break;
                if (!_events.Reader.TryRead(out pending)) continue;
            }
            QueuedEvent first = pending;
            pending = null;
            List<QueuedEvent> batchItems = [first];
            await Task.Delay(_batchWindow, cancellationToken).ConfigureAwait(false);
            while (batchItems.Count < ProductTelemetryPolicy.MaximumEventsPerBatch
                && _events.Reader.TryRead(out QueuedEvent? next))
            {
                if (next.Consent.Generation != first.Consent.Generation)
                {
                    pending = next;
                    break;
                }
                batchItems.Add(next);
            }
            HashSet<string> rateLimitKeys = [];
            List<QueuedEvent> eligibleItems = batchItems
                .Where(item => !IsRateLimitedEvent(item.Event)
                    || (!_rateLimits.WasSent(appVersion, item.Event)
                        && rateLimitKeys.Add(RateLimitKey(item.Event))))
                .ToList();
            if (eligibleItems.Count == 0) continue;
            List<ProductTelemetryEvent> batch = eligibleItems.Select(item => item.Event).ToList();
            if (!IsCurrent(first.Consent)
                || !await _ownerState.IsTelemetryDurablyEnabledAsync(cancellationToken)
                    .ConfigureAwait(false)) continue;
            try
            {
                using CancellationTokenSource sendCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, first.Consent.Token);
                Guid installId = await _installation.GetOrCreateAsync(sendCancellation.Token).ConfigureAwait(false);
                if (!IsCurrent(first.Consent)
                    || !await _ownerState.IsTelemetryDurablyEnabledAsync(sendCancellation.Token)
                        .ConfigureAwait(false)) continue;
                await _transport.SendAsync(
                    new ProductTelemetryBatch(
                        installId,
                        BuildVersion(),
                        PrivacyChoicesPolicy.CurrentNoticeVersion,
                        batch),
                    sendCancellation.Token).ConfigureAwait(false);
                await _rateLimits.MarkSentAsync(appVersion, batch, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException
                or UnauthorizedAccessException or InvalidDataException or JsonException
                or TaskCanceledException)
            {
                // Telemetry is best-effort and never interrupts automation or persists a retry queue.
            }
        }
    }

    private void OwnerState_OnPrivacyOptionsChanged(object? sender, EventArgs eventArgs)
    {
        if (_ownerState.TelemetryEnabled) return;
        lock (_choiceGate)
        {
            _choiceCancellation.Cancel();
            _retiredChoiceCancellations.Add(_choiceCancellation);
            _choiceCancellation = new CancellationTokenSource();
            _choiceGeneration++;
            while (_events.Reader.TryRead(out _)) { }
        }
    }

    private bool IsCurrent(Consent consent)
    {
        lock (_choiceGate)
        {
            return _ownerState.HasAcceptedCurrentPrivacyChoices
                && _ownerState.TelemetryEnabled
                && consent.Generation == _choiceGeneration
                && !consent.Token.IsCancellationRequested;
        }
    }

    private static bool IsRateLimitedEvent(ProductTelemetryEvent item) =>
        item.Kind is ProductTelemetryKind.OcrSetupFailure or ProductTelemetryKind.LocalInstanceFailure;

    private static string RateLimitKey(ProductTelemetryEvent item) =>
        $"{item.Kind}\0{item.Feature}\0{item.Outcome}\0{item.RequestedDevice ?? item.ConfigurationMode}";

    internal static ProductTelemetryEvent? Map(
        DeepDebugObservation observation,
        ProductTelemetryDeviceContext? deviceContext = null)
    {
        deviceContext ??= ProductTelemetryDeviceContext.Unknown;
        if (observation.Category is "macro" or "application"
            && observation.Action is "runtime_error" or "unhandled_exception")
        {
            return new ProductTelemetryEvent(
                ProductTelemetryKind.OperationError,
                observation.ObservedAtUtc,
                Feature: observation.Category,
                Outcome: observation.Action);
        }
        if (observation.Category == "ocr" && observation.Action == "inference_completed")
        {
            JsonElement data = JsonSerializer.SerializeToElement(observation.Data);
            string capability = ReadGraphicsCapability(data);
            return new ProductTelemetryEvent(
                ProductTelemetryKind.OcrTiming,
                observation.ObservedAtUtc,
                Feature: "ocr",
                Outcome: "completed",
                DurationMilliseconds: ReadInt(data, "InferenceMilliseconds"),
                GraphicsCapability: capability,
                HardwareModel: capability switch
                {
                    "gpu" or "gpu:0" => deviceContext.GraphicsModel,
                    "cpu" => deviceContext.ProcessorModel,
                    _ => null,
                });
        }
        if (observation.Category == "ocr_setup" && observation.Action == "setup_failed")
        {
            JsonElement data = JsonSerializer.SerializeToElement(observation.Data);
            string? code = ReadSafeText(data, "FailureCode");
            string? stage = ReadSafeText(data, "SetupStage");
            string? device = ReadSetupDevice(data);
            int? duration = ReadInt(data, "DurationMilliseconds");
            int? processExitCode = ReadInt(data, "ProcessExitCode");
            bool? pythonLauncherPresent = ReadBool(data, "PythonLauncherPresent");
            bool? wingetPresent = ReadBool(data, "WingetPresent");
            bool? existingOcrPythonPresent = ReadBool(data, "ExistingOcrPythonPresent");
            bool? runtimeMarkerPresent = ReadBool(data, "RuntimeMarkerPresent");
            if (!ProductTelemetryPolicy.IsOcrSetupFailureCode(code)
                || !ProductTelemetryPolicy.IsOcrSetupStage(stage)
                || device is not ("cpu" or "gpu:0")
                || duration is null
                || pythonLauncherPresent is null
                || wingetPresent is null
                || existingOcrPythonPresent is null
                || runtimeMarkerPresent is null)
                return null;
            return new ProductTelemetryEvent(
                ProductTelemetryKind.OcrSetupFailure,
                observation.ObservedAtUtc,
                Feature: "ocr-setup",
                Outcome: code,
                DurationMilliseconds: duration,
                OperatingSystem: OperatingSystemValue(),
                SetupStage: stage,
                RequestedDevice: device,
                ProcessExitCode: processExitCode,
                PythonLauncherPresent: pythonLauncherPresent,
                WingetPresent: wingetPresent,
                ExistingOcrPythonPresent: existingOcrPythonPresent,
                RuntimeMarkerPresent: runtimeMarkerPresent);
        }
        if (observation.Category == "local_instance" && observation.Action == "operation_failed")
        {
            JsonElement data = JsonSerializer.SerializeToElement(observation.Data);
            string? operation = ReadSafeText(data, "Operation");
            string? code = ReadSafeText(data, "FailureCode");
            string? configurationMode = ReadSafeText(data, "ConfigurationMode");
            int? duration = ReadInt(data, "DurationMilliseconds");
            int? processExitCode = ReadInt(data, "ProcessExitCode");
            int? runnerCount = ReadInt(data, "RunnerCount");
            if (!ProductTelemetryPolicy.IsLocalInstanceOperation(operation)
                || !ProductTelemetryPolicy.IsLocalInstanceFailureCode(code)
                || configurationMode is not ("shared" or "isolated" or "not-applicable")
                || duration is null
                || runnerCount is null)
                return null;
            return new ProductTelemetryEvent(
                ProductTelemetryKind.LocalInstanceFailure,
                observation.ObservedAtUtc,
                Feature: "local-instance",
                Outcome: code,
                DurationMilliseconds: duration,
                OperatingSystem: OperatingSystemValue(),
                ProcessExitCode: processExitCode,
                Operation: operation,
                FailureCode: code,
                ConfigurationMode: configurationMode,
                RunnerCount: runnerCount);
        }
        if (observation.Category == "route_optimizer_test" && observation.Action == "trial_observed")
        {
            JsonElement data = JsonSerializer.SerializeToElement(observation.Data);
            return new ProductTelemetryEvent(
                ProductTelemetryKind.ExpeditionRewardObserved,
                observation.ObservedAtUtc,
                Feature: "route-optimizer",
                Outcome: "observed",
                Material: ReadSafeText(data, "Target"),
                Quantity: ReadInt(data, "Quantity"));
        }
        if (observation.Category == "ui_scale" && observation.Action == "ui_scale_feedback")
        {
            JsonElement data = JsonSerializer.SerializeToElement(observation.Data);
            double? input = ReadDouble(data, "Candidate");
            double? rendered = ReadDouble(data, "ObservedRenderedScale");
            if (deviceContext.DisplayWidth is < 640 or > 16_384
                || deviceContext.DisplayHeight is < 480 or > 16_384
                || input is null or < 0.8 or > 1.2
                || rendered is null or < 0.5 or > 1.5)
                return null;
            return new ProductTelemetryEvent(
                ProductTelemetryKind.UiScaleCalibration,
                observation.ObservedAtUtc,
                Feature: "ui-scale",
                Outcome: "observed",
                DisplayWidth: deviceContext.DisplayWidth,
                DisplayHeight: deviceContext.DisplayHeight,
                InputScaleMilli: (int)Math.Round(input.Value * 1000, MidpointRounding.AwayFromZero),
                RenderedScaleMilli: (int)Math.Round(rendered.Value * 1000, MidpointRounding.AwayFromZero));
        }
        if (observation.Category is "workspace" or "wire" or "challenge" or "game_settings" or "ui_scale"
            && observation.Action.EndsWith("completed", StringComparison.Ordinal))
        {
            return new ProductTelemetryEvent(
                ProductTelemetryKind.FeatureUsed,
                observation.ObservedAtUtc,
                Feature: observation.Category,
                Outcome: "completed");
        }
        return null;
    }

    private static int? ReadInt(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value)
            && value.TryGetInt32(out int result) ? result : null;

    private static double? ReadDouble(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value)
            && value.TryGetDouble(out double result) && double.IsFinite(result) ? result : null;

    private static bool? ReadBool(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

    private static string? ReadSafeText(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out JsonElement value)) return null;
        string? text = value.GetString();
        return text is { Length: >= 1 and <= 48 }
            && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ' ')
            ? text : null;
    }

    private static string? ReadSetupDevice(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("Device", out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString()?.ToLowerInvariant() is "cpu" or "gpu:0"
            ? value.GetString()!.ToLowerInvariant()
            : null;
    }

    private static string ReadGraphicsCapability(JsonElement data)
    {
        string? device = data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("Device", out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.ToLowerInvariant()
            : null;
        return device switch
        {
            "cpu" => "cpu",
            "gpu" => "gpu",
            "gpu:0" or "gpu_0" => "gpu:0",
            _ => "not-observed",
        };
    }

    private static string OperatingSystemValue() =>
        $"windows-{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";

    private static string BuildVersion() =>
        typeof(ProductTelemetryService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
