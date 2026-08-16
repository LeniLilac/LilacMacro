using System.Text.Json;
using System.Text.Json.Serialization;
using LilacMacro.Core.Services;

namespace LilacMacro.Runtime.Services;

public sealed class ProductTelemetryRateLimitStore
{
    private const int SchemaVersion = 1;
    private const int MaximumEntries = 256;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
    private readonly string _path;
    private readonly object _gate = new();
    private HashSet<RateLimitKey> _sent = [];
    private bool _loaded;

    public ProductTelemetryRateLimitStore(string configurationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRoot);
        _path = Path.Combine(
            Path.GetFullPath(configurationRoot),
            "services",
            "product-telemetry-rate-limits.json");
    }

    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_loaded) return;
        }

        HashSet<RateLimitKey> loaded = [];
        try
        {
            await using FileStream writeLock = await AcquireLockAsync(
                _path + ".write.lock", cancellationToken).ConfigureAwait(false);
            foreach (RateLimitEntry entry in await ReadEntriesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (IsValid(entry)) loaded.Add(new RateLimitKey(entry.AppVersion, entry.Feature, entry.Outcome, entry.Scope));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or JsonException or InvalidDataException)
        {
            // A rate-limit ledger is optional local metadata; a read failure must not block telemetry.
        }

        lock (_gate)
        {
            if (!_loaded)
            {
                _sent = loaded;
                _loaded = true;
            }
        }
    }

    internal bool WasSent(string appVersion, ProductTelemetryEvent item)
    {
        if (!TryCreateKey(appVersion, item, out RateLimitKey key)) return false;
        lock (_gate) return _sent.Contains(key);
    }

    internal async Task MarkSentAsync(
        string appVersion,
        IReadOnlyList<ProductTelemetryEvent> events,
        CancellationToken cancellationToken = default)
    {
        HashSet<RateLimitKey> keys = events
            .Select(item => TryCreateKey(appVersion, item, out RateLimitKey key) ? key : (RateLimitKey?)null)
            .Where(key => key is not null)
            .Select(key => key!.Value)
            .ToHashSet();
        if (keys.Count == 0) return;

        try
        {
            string directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Telemetry rate-limit path has no parent.");
            Directory.CreateDirectory(directory);
            await using FileStream writeLock = await AcquireLockAsync(
                _path + ".write.lock", cancellationToken).ConfigureAwait(false);
            Dictionary<RateLimitKey, DateTimeOffset> entries = [];
            foreach (RateLimitEntry entry in await ReadEntriesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (IsValid(entry))
                    entries[new RateLimitKey(entry.AppVersion, entry.Feature, entry.Outcome, entry.Scope)] = entry.SentAtUtc;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (RateLimitKey key in keys) entries.TryAdd(key, now);
            List<RateLimitEntry> documentEntries = entries
                .OrderByDescending(item => item.Value)
                .Take(MaximumEntries)
                .OrderBy(item => item.Value)
                .Select(item => new RateLimitEntry(
                    item.Key.AppVersion,
                    item.Key.Feature,
                    item.Key.Outcome,
                    item.Key.Scope,
                    item.Value))
                .ToList();
            await WriteAsync(new RateLimitDocument(SchemaVersion, documentEntries), cancellationToken)
                .ConfigureAwait(false);

            lock (_gate)
            {
                _sent.UnionWith(keys);
                _sent = _sent
                    .Where(item => documentEntries.Any(entry =>
                        entry.AppVersion == item.AppVersion
                        && entry.Feature == item.Feature
                        && entry.Outcome == item.Outcome
                        && entry.Scope == item.Scope))
                    .ToHashSet();
                _loaded = true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or JsonException or InvalidDataException or OperationCanceledException)
        {
            // Telemetry is best-effort; failure to persist the dedupe marker is harmless.
        }
    }

    private async Task<IReadOnlyList<RateLimitEntry>> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        await using FileStream stream = File.OpenRead(_path);
        RateLimitDocument? document = await JsonSerializer.DeserializeAsync<RateLimitDocument>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (document is null || document.SchemaVersion != SchemaVersion
            || document.Entries is null || document.Entries.Count > MaximumEntries)
            throw new InvalidDataException("Telemetry rate-limit ledger was invalid.");
        return document.Entries;
    }

    private async Task WriteAsync(RateLimitDocument document, CancellationToken cancellationToken)
    {
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsValid(RateLimitEntry entry) =>
        entry.AppVersion is { Length: >= 5 and <= 32 }
        && entry.Feature is "ocr-setup" or "local-instance"
        && entry.Outcome is not null
        && (entry.Feature == "ocr-setup"
            ? ProductTelemetryPolicy.IsOcrSetupFailureCode(entry.Outcome)
            : ProductTelemetryPolicy.IsLocalInstanceFailureCode(entry.Outcome))
        && entry.Scope is "cpu" or "gpu:0" or "shared" or "isolated" or "not-applicable"
        && entry.SentAtUtc != default;

    private static bool TryCreateKey(
        string appVersion,
        ProductTelemetryEvent item,
        out RateLimitKey key)
    {
        if (item.Kind == ProductTelemetryKind.OcrSetupFailure
            && item.Feature is not null
            && item.Outcome is not null
            && item.RequestedDevice is not null)
        {
            key = new RateLimitKey(appVersion, item.Feature, item.Outcome, item.RequestedDevice);
            return true;
        }
        if (item.Kind == ProductTelemetryKind.LocalInstanceFailure
            && item.Feature is not null
            && item.Outcome is not null
            && item.ConfigurationMode is not null)
        {
            key = new RateLimitKey(appVersion, item.Feature, item.Outcome, item.ConfigurationMode);
            return true;
        }
        key = default;
        return false;
    }

    private static async Task<FileStream> AcquireLockAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Telemetry rate-limit lock path has no parent.");
        Directory.CreateDirectory(directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private readonly record struct RateLimitKey(string AppVersion, string Feature, string Outcome, string Scope);

    private sealed record RateLimitDocument(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("entries")] IReadOnlyList<RateLimitEntry> Entries);

    private sealed record RateLimitEntry(
        [property: JsonPropertyName("app_version")] string AppVersion,
        [property: JsonPropertyName("feature")] string Feature,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("sent_at_utc")] DateTimeOffset SentAtUtc);
}
