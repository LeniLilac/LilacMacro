using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal sealed record LightDiagnosticEvent(
    DateTimeOffset ObservedAtUtc,
    string Category,
    string Action,
    JsonElement? Data);

internal sealed record LightDiagnosticFrame(
    DateTimeOffset ObservedAtUtc,
    string Source,
    byte[] PngBytes);

internal sealed record LightDiagnosticSnapshot(
    IReadOnlyList<LightDiagnosticEvent> Events,
    IReadOnlyList<LightDiagnosticFrame> Frames);

internal sealed class LightDiagnosticBuffer
{
    internal const int MaximumFrames = 32;
    internal const int MaximumEvents = 256;
    internal const long MaximumBufferedFrameBytes = 70L * 1024 * 1024;
    private const int MaximumSingleFrameBytes = 8 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Queue<LightDiagnosticEvent> _events = new();
    private readonly Queue<LightDiagnosticFrame> _frames = new();
    private long _frameBytes;

    internal void Capture(DeepDebugObservation observation)
    {
        lock (_gate)
        {
            _events.Enqueue(new LightDiagnosticEvent(
                observation.ObservedAtUtc,
                SafeToken(observation.Category),
                SafeToken(observation.Action),
                SelectSafeData(observation)));
            while (_events.Count > MaximumEvents) _events.Dequeue();
            if (observation.PngBytes is not { Length: > 0 } png
                || png.Length > MaximumSingleFrameBytes) return;
            string source = SafeToken(observation.Action);
            _frames.Enqueue(new LightDiagnosticFrame(observation.ObservedAtUtc, source, png));
            _frameBytes += png.Length;
            while (_frames.Count > MaximumFrames || _frameBytes > MaximumBufferedFrameBytes)
                _frameBytes -= _frames.Dequeue().PngBytes.Length;
        }
    }

    internal LightDiagnosticSnapshot SnapshotAndClear()
    {
        lock (_gate)
        {
            LightDiagnosticSnapshot snapshot = new(_events.ToArray(), _frames.ToArray());
            _events.Clear();
            _frames.Clear();
            _frameBytes = 0;
            return snapshot;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
            _frames.Clear();
            _frameBytes = 0;
        }
    }

    private static JsonElement? SelectSafeData(DeepDebugObservation observation)
    {
        if (observation.Data is null) return null;
        JsonElement source = JsonSerializer.SerializeToElement(observation.Data);
        if (source.ValueKind != JsonValueKind.Object) return null;
        string[] allowed = observation.Category switch
        {
            "ocr" => ["ModelName", "DetectorModelName", "Device", "ModelLoadMilliseconds", "InferenceMilliseconds", "ModelCached", "Confidence"],
            "route_optimizer_test" => ["Difficulty", "Target", "Quantity", "Threshold", "Decision", "RerollMilliseconds", "CompletePool", "Quantities"],
            "vision" => ["ProfileId", "Revision"],
            "session" => ["operation", "Surface", "Outcome"],
            _ => [],
        };
        Dictionary<string, JsonElement> selected = [];
        foreach (string name in allowed)
        {
            if (source.TryGetProperty(name, out JsonElement value) && IsBoundedValue(value))
                selected[name] = value.Clone();
        }
        return selected.Count == 0 ? null : JsonSerializer.SerializeToElement(selected);
    }

    private static bool IsBoundedValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() is { Length: <= 96 },
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
        JsonValueKind.Object => value.EnumerateObject().Take(17).Count() <= 16
            && value.EnumerateObject().All(property =>
                property.Name.Length <= 48 && IsBoundedValue(property.Value)),
        _ => false,
    };

    private static string SafeToken(string value)
    {
        string token = new(value.Take(64).Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        return string.IsNullOrWhiteSpace(token) ? "unknown" : token;
    }
}
