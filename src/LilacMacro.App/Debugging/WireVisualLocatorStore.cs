using System.Text.Json;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Debugging;

internal sealed record WireVisualLocator(
    int Version,
    string ProfileId,
    string State,
    string Label,
    PixelRect Bounds);

internal sealed class WireVisualLocatorStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<string> SaveAsync(
        string root,
        WireVisualLocator locator,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(locator);
        string path = PathFor(root, locator.ProfileId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             true))
            {
                await JsonSerializer.SerializeAsync(stream, locator, Json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, path, overwrite: true);
            if (!File.Exists(path)) throw new IOException("Visual locator was not persisted.");
            return path;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<WireVisualLocator> LoadAsync(
        string root,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(PathFor(root, profileId));
        return await JsonSerializer.DeserializeAsync<WireVisualLocator>(stream, Json, cancellationToken)
            ?? throw new InvalidDataException("Visual locator is empty.");
    }

    public string PathFor(string root, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        new Core.Vision.VisualAnchorDefinition(profileId, [profileId]).Validate();
        if (profileId is "." or ".."
            || profileId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || profileId.Contains(Path.DirectorySeparatorChar)
            || profileId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Visual locator profile identifier is unsafe.", nameof(profileId));
        }

        string fullRoot = Path.GetFullPath(root);
        string profileRoot = Path.GetFullPath(Path.Combine(fullRoot, profileId));
        string requiredPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!profileRoot.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Visual locator profile escapes its storage root.", nameof(profileId));
        return Path.Combine(profileRoot, "locator.json");
    }
}
