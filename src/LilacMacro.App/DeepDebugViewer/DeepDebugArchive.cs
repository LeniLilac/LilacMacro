using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LilacMacro.App.DeepDebugViewer;

internal sealed partial class DeepDebugArchive : IDisposable
{
    private const long MaximumFrameBytes = 64L * 1024 * 1024;
    private const long MaximumJsonBytes = 128L * 1024 * 1024;
    private const int MaximumEntries = 100_000;
    private const int MaximumDetailsCharacters = 2400;
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly IReadOnlyDictionary<string, ZipArchiveEntry> _entries;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private bool _disposed;

    private DeepDebugArchive(
        string path,
        FileStream stream,
        ZipArchive archive,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        DeepDebugArchiveIndex index)
    {
        Path = path;
        _stream = stream;
        _archive = archive;
        _entries = entries;
        Manifest = index.Manifest;
        Events = index.Events;
        Frames = index.Frames;
        MalformedEventLines = index.MalformedEventLines;
    }

    public string Path { get; }
    public DeepDebugManifestSummary Manifest { get; }
    public IReadOnlyList<DeepDebugTimelineEvent> Events { get; }
    public IReadOnlyList<DeepDebugFrameRecord> Frames { get; }
    public int MalformedEventLines { get; }

    public static async Task<DeepDebugArchive> OpenAsync(
        string path,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Deep Debug ZIP not found.", fullPath);
        if (!fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a .zip Deep Debug archive.");

        return await Task.Run(async () =>
        {
            FileStream? stream = null;
            ZipArchive? archive = null;
            try
            {
                progress?.Report("OPENING ZIP INDEX");
                stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.RandomAccess);
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                if (archive.Entries.Count > MaximumEntries)
                    throw new InvalidDataException($"Archive has too many entries ({archive.Entries.Count:N0}).");
                Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entries.TryAdd(NormalizePath(entry.FullName), entry);
                }

                DeepDebugManifestSummary manifest = await ReadManifestAsync(entries, cancellationToken);
                progress?.Report("INDEXING EVENTS + FRAMES");
                DeepDebugArchiveIndex index = await ReadTimelineAsync(entries, manifest, progress, cancellationToken);
                if (index.Frames.Count == 0)
                    index = index with { Frames = BuildFallbackFrames(entries) };
                DeepDebugArchive result = new(fullPath, stream, archive, entries, index);
                stream = null;
                archive = null;
                return result;
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"Unreadable Deep Debug archive. {error.Message}", error);
            }
            finally
            {
                archive?.Dispose();
                stream?.Dispose();
            }
        }, cancellationToken);
    }

    public IReadOnlyList<DeepDebugTimelineEvent> GetNearbyEvents(int frameIndex, int before = 12, int after = 20)
    {
        if (frameIndex < 0 || frameIndex >= Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (Events.Count == 0) return [];
        int center = Math.Clamp(Frames[frameIndex].EventIndex, 0, Events.Count - 1);
        int start = Math.Max(0, center - before);
        return Events.Skip(start).Take(Math.Min(Events.Count - start, before + after + 1)).ToArray();
    }

    public IReadOnlyList<DeepDebugInputMarker> GetInputMarkers(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= Frames.Count) return [];
        DeepDebugFrameRecord frame = Frames[frameIndex];
        long nextSequence = frameIndex + 1 < Frames.Count ? Frames[frameIndex + 1].Sequence : long.MaxValue;
        List<DeepDebugInputMarker> markers = [];
        foreach (DeepDebugTimelineEvent item in Events)
        {
            if (item.Sequence <= frame.Sequence || item.Sequence > nextSequence) continue;
            bool click = IsInputStart(item.Action, "click");
            bool scroll = IsInputStart(item.Action, "scroll");
            if ((!click && !scroll) || !TryFindPoint(item.Data, out int x, out int y)) continue;
            DeepDebugSourceRegion region = frame.SourceRegion ?? new(0, 0, int.MaxValue, int.MaxValue);
            if (!region.Contains(x, y)) continue;
            markers.Add(new DeepDebugInputMarker(
                markers.Count + 1,
                scroll ? "SCROLL" : "CLICK",
                x,
                y,
                x - region.X,
                y - region.Y,
                scroll && TryFindInteger(item.Data, "wheelDelta", out int delta) ? delta : null,
                item.TimestampUtc));
        }
        return markers;
    }

    private static bool IsInputStart(string action, string kind) =>
        action.Equals(kind, StringComparison.OrdinalIgnoreCase) ||
        action.Equals($"{kind}_started", StringComparison.OrdinalIgnoreCase) ||
        action.Equals($"{kind}-started", StringComparison.OrdinalIgnoreCase);

    public async Task<byte[]> ReadFrameBytesAsync(DeepDebugFrameRecord frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.TryGetValue(NormalizePath(frame.Path), out ZipArchiveEntry? entry))
            throw new InvalidDataException($"Frame '{frame.Path}' is missing from the archive.");
        if (entry.Length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException($"Frame '{frame.Path}' has invalid size {entry.Length:N0} bytes.");
        await _readGate.WaitAsync(cancellationToken);
        try
        {
            await using Stream input = entry.Open();
            using MemoryStream output = new((int)entry.Length);
            await input.CopyToAsync(output, cancellationToken);
            return output.ToArray();
        }
        finally { _readGate.Release(); }
    }

    public void Dispose()
    {
        _readGate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _archive.Dispose();
            _stream.Dispose();
        }
        finally { _readGate.Release(); }
    }

    private static async Task<DeepDebugManifestSummary> ReadManifestAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries, CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue("manifest.json", out ZipArchiveEntry? entry))
            return new("DEEP DEBUG", "UNKNOWN", "UNKNOWN", null, null, null, 0, 0, 0, 0);
        EnsureJsonSize(entry);
        await using Stream stream = entry.Open();
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        return new(
            GetString(root, "operation") ?? "DEEP DEBUG",
            GetString(root, "outcome") ?? "UNKNOWN",
            GetString(root, "appVersion", "app_version") ?? "UNKNOWN",
            GetDate(root, "startedAtUtc", "started_at_utc"),
            GetDate(root, "completedAtUtc", "completed_at_utc"),
            GetTimeSpan(root, "runtime"),
            GetInt(root, "artifacts", "frames"),
            GetInt(root, "events"),
            GetInt(root, "inputEvents", "input_events"),
            GetInt(root, "visualProfiles", "visual_profiles"));
    }

    private static async Task<DeepDebugArchiveIndex> ReadTimelineAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        DeepDebugManifestSummary manifest,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue("events.jsonl", out ZipArchiveEntry? entry))
            return new(manifest, [], [], 0);
        EnsureJsonSize(entry);
        List<(DeepDebugTimelineEvent Item, int Order)> parsed = [];
        int malformed = 0;
        int lineNumber = 0;
        await using Stream stream = entry.Open();
        using StreamReader reader = new(stream, Encoding.UTF8, true, 128 * 1024);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                JsonElement data = TryProperty(root, out JsonElement found, "data") ? found.Clone() : default;
                parsed.Add((new(
                    GetLong(root, lineNumber, "sequence"),
                    GetDate(root, "timestampUtc", "timestamp_utc") ?? DateTimeOffset.UnixEpoch.AddMilliseconds(lineNumber),
                    GetString(root, "category") ?? "unknown",
                    GetString(root, "action") ?? "unknown",
                    NormalizeOptionalPath(GetString(root, "artifact", "frame")),
                    SummarizeData(data),
                    data), lineNumber));
            }
            catch (JsonException) { malformed++; }
            if (lineNumber % 1000 == 0) progress?.Report($"INDEXED {lineNumber:N0} EVENTS");
        }
        DeepDebugTimelineEvent[] events = parsed.OrderBy(value => value.Item.Sequence)
            .ThenBy(value => value.Item.TimestampUtc).ThenBy(value => value.Order)
            .Select(value => value.Item).ToArray();
        List<DeepDebugFrameRecord> frames = [];
        for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
        {
            DeepDebugTimelineEvent item = events[eventIndex];
            if (item.ArtifactPath is null || !IsFramePath(item.ArtifactPath)) continue;
            if (!item.Category.Equals("frame", StringComparison.OrdinalIgnoreCase) &&
                !item.ArtifactPath.StartsWith("frames/", StringComparison.OrdinalIgnoreCase)) continue;
            frames.Add(new(frames.Count, item.Sequence, item.TimestampUtc, item.ArtifactPath,
                eventIndex, entries.ContainsKey(item.ArtifactPath), FindSourceRegion(item.Data)));
        }
        return new(manifest, events, frames, malformed);
    }

    private static IReadOnlyList<DeepDebugFrameRecord> BuildFallbackFrames(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries) => entries.Values
        .Where(entry => NormalizePath(entry.FullName).StartsWith("frames/", StringComparison.OrdinalIgnoreCase))
        .Where(entry => IsFramePath(entry.FullName))
        .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
        .Select((entry, index) => new DeepDebugFrameRecord(index, index + 1, entry.LastWriteTime,
            NormalizePath(entry.FullName), 0, true, null)).ToArray();

    private static bool IsFramePath(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".avif", StringComparison.OrdinalIgnoreCase);

    private static DeepDebugSourceRegion? FindSourceRegion(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        foreach (string name in new[] { "crop", "region", "size", "requiredSize", "observedClientSize" })
        {
            if (!TryProperty(data, out JsonElement value, name) || value.ValueKind != JsonValueKind.Object) continue;
            int x = GetInt(value, "x");
            int y = GetInt(value, "y");
            int width = GetInt(value, "width");
            int height = GetInt(value, "height");
            if (width > 0 && height > 0) return new(x, y, width, height);
        }
        return null;
    }

    private static bool TryFindPoint(JsonElement element, out int x, out int y)
    {
        x = y = 0;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (TryProperty(element, out JsonElement point, "point") && point.ValueKind == JsonValueKind.Object &&
            TryGetInt(point, "x", out x) && TryGetInt(point, "y", out y)) return true;
        if (TryGetInt(element, "x", out x) && TryGetInt(element, "y", out y)) return true;
        foreach (JsonProperty property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object && TryFindPoint(property.Value, out x, out y)) return true;
        return false;
    }

    private static bool TryFindInteger(JsonElement element, string name, out int result)
    {
        result = 0;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (TryGetInt(element, name, out result)) return true;
        foreach (JsonProperty property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object && TryFindInteger(property.Value, name, out result)) return true;
        return false;
    }

    private static string SummarizeData(JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return string.Empty;
        string result = data.ValueKind == JsonValueKind.Object
            ? string.Join("  ", data.EnumerateObject().Select(property => $"{property.Name}={Compact(property.Value)}"))
            : Compact(data);
        result = Redact(result);
        return result.Length <= MaximumDetailsCharacters ? result : result[..MaximumDetailsCharacters] + "...";
    }

    private static string Compact(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    private static string Redact(string value)
    {
        string result = DiscordWebhookRegex().Replace(value, "[REDACTED WEBHOOK]");
        return PrivateServerRegex().Replace(result, "privateServerLinkCode=[REDACTED]");
    }

    private static void EnsureJsonSize(ZipArchiveEntry entry)
    {
        if (entry.Length < 0 || entry.Length > MaximumJsonBytes)
            throw new InvalidDataException($"'{entry.FullName}' exceeds the bounded JSON size.");
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string? NormalizeOptionalPath(string? path) => string.IsNullOrWhiteSpace(path) ? null : NormalizePath(path);
    private static bool TryProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (string name in names) if (element.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
    private static string? GetString(JsonElement element, params string[] names) =>
        TryProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int GetInt(JsonElement element, params string[] names) => TryProperty(element, out JsonElement value, names) && value.TryGetInt32(out int result) ? result : 0;
    private static bool TryGetInt(JsonElement element, string name, out int result)
    {
        result = 0;
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out result);
    }
    private static long GetLong(JsonElement element, long fallback, params string[] names) => TryProperty(element, out JsonElement value, names) && value.TryGetInt64(out long result) ? result : fallback;
    private static DateTimeOffset? GetDate(JsonElement element, params string[] names) =>
        TryProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset result) ? result : null;
    private static TimeSpan? GetTimeSpan(JsonElement element, params string[] names) =>
        TryProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.String &&
        TimeSpan.TryParse(value.GetString(), CultureInfo.InvariantCulture, out TimeSpan result) ? result : null;

    [GeneratedRegex("https://(?:[a-z0-9-]+\\.)?discord(?:app)?\\.com/api(?:/v[0-9]+)?/webhooks/[^\\s\\\"'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex DiscordWebhookRegex();

    [GeneratedRegex("privateServerLinkCode=[^\\s\\\"'&<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateServerRegex();
}
