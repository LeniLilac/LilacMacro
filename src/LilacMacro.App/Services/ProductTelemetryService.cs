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
    private readonly IProductTelemetryTransport _transport;
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
        TimeSpan? batchWindow = null)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _installation = installation;
        _transport = transport;
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
        ProductTelemetryEvent? item = Map(observation);
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
            List<ProductTelemetryEvent> batch = [first.Event];
            await Task.Delay(_batchWindow, cancellationToken).ConfigureAwait(false);
            while (batch.Count < ProductTelemetryPolicy.MaximumEventsPerBatch
                && _events.Reader.TryRead(out QueuedEvent? next))
            {
                if (next.Consent.Generation != first.Consent.Generation)
                {
                    pending = next;
                    break;
                }
                batch.Add(next.Event);
            }
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

    private static ProductTelemetryEvent? Map(DeepDebugObservation observation)
    {
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
            return new ProductTelemetryEvent(
                ProductTelemetryKind.OcrTiming,
                observation.ObservedAtUtc,
                Feature: "ocr",
                Outcome: "completed",
                DurationMilliseconds: ReadInt(data, "InferenceMilliseconds"),
                GraphicsCapability: ReadGraphicsCapability(data));
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

    private static string? ReadSafeText(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out JsonElement value)) return null;
        string? text = value.GetString();
        return text is { Length: >= 1 and <= 48 }
            && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ' ')
            ? text : null;
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
