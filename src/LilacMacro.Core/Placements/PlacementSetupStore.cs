using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilacMacro.Core.Placements;

public sealed class PlacementSetupStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly string _rootDirectory;

    public PlacementSetupStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task<PlacementSetupDocument> LoadOrCreateAsync(
        string mapId,
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken = default)
    {
        string path = PathFor(mapId);
        if (!File.Exists(path)) return PlacementSetupRules.CreateDocument(mapId, imageWidth, imageHeight);
        await using FileStream stream = File.OpenRead(path);
        PlacementSetupDocument document = await JsonSerializer.DeserializeAsync<PlacementSetupDocument>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Placement setup file is empty.");
        PlacementSetupRules.Validate(document);
        if (!string.Equals(document.MapId, mapId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Placement setup map id does not match its file.");
        }
        if (document.ImageWidth != imageWidth || document.ImageHeight != imageHeight)
        {
            throw new InvalidDataException(
                $"Saved setup uses {document.ImageWidth} x {document.ImageHeight}; map uses {imageWidth} x {imageHeight}.");
        }
        return document;
    }

    public async Task<PlacementSetupDocument> LoadAsync(
        string mapId,
        CancellationToken cancellationToken = default)
    {
        string path = PathFor(mapId);
        if (!File.Exists(path)) throw new FileNotFoundException("Placement setup was not found.", path);
        await using FileStream stream = File.OpenRead(path);
        PlacementSetupDocument document = await JsonSerializer.DeserializeAsync<PlacementSetupDocument>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Placement setup file is empty.");
        PlacementSetupRules.Validate(document);
        if (!string.Equals(document.MapId, mapId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Placement setup map id does not match its file.");
        }
        return document;
    }

    public async Task SaveAsync(PlacementSetupDocument document, CancellationToken cancellationToken = default)
    {
        PlacementSetupRules.Validate(document);
        Directory.CreateDirectory(_rootDirectory);
        string destination = PathFor(document.MapId);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<PlacementSetupBatch> BeginBatchAsync(
        IReadOnlyCollection<PlacementSetupDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) return new PlacementSetupBatch([]);
        HashSet<string> mapIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlacementSetupDocument document in documents)
        {
            PlacementSetupRules.Validate(document);
            if (!mapIds.Add(document.MapId))
                throw new InvalidDataException("Placement batch contains a duplicate map.");
        }

        Directory.CreateDirectory(_rootDirectory);
        List<PlacementSetupBatchEntry> entries = [];
        try
        {
            foreach (PlacementSetupDocument document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = PathFor(document.MapId);
                string identity = Guid.NewGuid().ToString("N");
                string temporary = destination + $".{identity}.share.tmp";
                string? backup = File.Exists(destination)
                    ? destination + $".{identity}.share.bak"
                    : null;
                await WriteDocumentAsync(temporary, document, cancellationToken).ConfigureAwait(false);
                if (backup is not null) File.Copy(destination, backup, overwrite: false);
                entries.Add(new PlacementSetupBatchEntry(destination, temporary, backup));
            }
            foreach (PlacementSetupBatchEntry entry in entries)
            {
                File.Move(entry.Temporary, entry.Destination, overwrite: true);
                entry.Applied = true;
            }
            return new PlacementSetupBatch(entries);
        }
        catch
        {
            PlacementSetupBatch.RollBack(entries);
            throw;
        }
    }

    private string PathFor(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId) ||
            !mapId.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new ArgumentException("Placement map id is invalid.", nameof(mapId));
        }
        return Path.Combine(_rootDirectory, $"{mapId}.json");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static async Task WriteDocumentAsync(
        string path,
        PlacementSetupDocument document,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            useAsync: true);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PlacementSetupBatch : IDisposable
{
    private readonly IReadOnlyList<PlacementSetupBatchEntry> _entries;
    private bool _committed;
    private bool _disposed;

    internal PlacementSetupBatch(IReadOnlyList<PlacementSetupBatchEntry> entries) => _entries = entries;

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _committed = true;
        Clean(_entries);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_committed) RollBack(_entries);
        else Clean(_entries);
    }

    internal static void RollBack(IEnumerable<PlacementSetupBatchEntry> entries)
    {
        Exception? firstFailure = null;
        foreach (PlacementSetupBatchEntry entry in entries.Reverse())
        {
            bool restored = !entry.Applied;
            try
            {
                if (entry.Applied)
                {
                    if (entry.Backup is not null) File.Move(entry.Backup, entry.Destination, overwrite: true);
                    else if (File.Exists(entry.Destination)) File.Delete(entry.Destination);
                    restored = true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                firstFailure ??= exception;
            }
            finally
            {
                DeleteIfPresent(entry.Temporary);
                if (restored && entry.Backup is not null) DeleteIfPresent(entry.Backup);
            }
        }
        if (firstFailure is not null)
            throw new IOException("A placement import could not restore every prior file; backup files were retained.", firstFailure);
    }

    private static void Clean(IEnumerable<PlacementSetupBatchEntry> entries)
    {
        foreach (PlacementSetupBatchEntry entry in entries)
        {
            DeleteIfPresent(entry.Temporary);
            if (entry.Backup is not null) DeleteIfPresent(entry.Backup);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed record PlacementSetupBatchEntry(string Destination, string Temporary, string? Backup)
{
    public bool Applied { get; set; }
}
