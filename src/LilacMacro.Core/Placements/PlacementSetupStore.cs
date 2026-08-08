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
}
