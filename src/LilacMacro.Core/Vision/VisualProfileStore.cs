using System.Security.Cryptography;
using System.Text.Json;
using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Vision;

public sealed class VisualProfileStore
{
    public const string ManifestFileName = "profile.json";

    public async Task<string> SaveRevisionAsync(
        string rootDirectory,
        VisualAnchorProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        string profileRoot = Path.Combine(Path.GetFullPath(rootDirectory), profile.Definition.Id);
        string revisionsRoot = Path.Combine(profileRoot, "revisions");
        Directory.CreateDirectory(revisionsRoot);
        string revisionName = $"{profile.BuiltAtUtc:yyyyMMddTHHmmssfffZ}-{profile.RevisionId:N}";
        string destination = Path.Combine(revisionsRoot, revisionName);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Visual profile revision already exists: {destination}");
        }

        try
        {
            Directory.CreateDirectory(temporary);
            VisualProfileManifest manifest = await WriteAssetsAsync(temporary, profile, cancellationToken)
                .ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(temporary, ManifestFileName), manifest, cancellationToken)
                .ConfigureAwait(false);
            Directory.Move(temporary, destination);
            await WriteJsonAtomicallyAsync(
                Path.Combine(profileRoot, "current.json"),
                new CurrentRevision(revisionName),
                cancellationToken).ConfigureAwait(false);
            return destination;
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    public async Task<VisualAnchorProfile> LoadCurrentAsync(
        string rootDirectory,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        LoadedVisualProfileRevision loaded = await LoadCurrentRevisionAsync(
            rootDirectory,
            profileId,
            cancellationToken).ConfigureAwait(false);
        return loaded.Profile;
    }

    public async Task<LoadedVisualProfileRevision> LoadCurrentRevisionAsync(
        string rootDirectory,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        new VisualAnchorDefinition(profileId, [profileId]).Validate();
        string profileRoot = Path.Combine(Path.GetFullPath(rootDirectory), profileId);
        CurrentRevision pointer = await ReadJsonAsync<CurrentRevision>(
            Path.Combine(profileRoot, "current.json"), cancellationToken).ConfigureAwait(false);
        if (Path.GetFileName(pointer.Revision) != pointer.Revision)
        {
            throw new InvalidDataException("Visual profile revision pointer is invalid.");
        }

        string revisionDirectory = Path.Combine(profileRoot, "revisions", pointer.Revision);
        VisualAnchorProfile profile = await LoadRevisionAsync(
            revisionDirectory,
            cancellationToken).ConfigureAwait(false);
        return new LoadedVisualProfileRevision(profile, revisionDirectory);
    }

    public async Task<VisualAnchorProfile> LoadRevisionAsync(
        string revisionDirectory,
        CancellationToken cancellationToken = default)
    {
        string fullDirectory = Path.GetFullPath(revisionDirectory);
        VisualProfileManifest manifest = await ReadJsonAsync<VisualProfileManifest>(
            Path.Combine(fullDirectory, ManifestFileName), cancellationToken).ConfigureAwait(false);
        ValidateManifest(manifest);
        Dictionary<string, GrayImage> assets = [];
        foreach ((string key, string fileName) in manifest.Assets)
        {
            if (Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".pgm", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Visual profile contains an unsafe asset path.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(fullDirectory, fileName), cancellationToken)
                .ConfigureAwait(false);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!manifest.Sha256.TryGetValue(key, out string? expected) ||
                !string.Equals(hash, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Visual profile asset hash mismatch: {fileName}");
            }

            assets.Add(key, PortableGraymap.Decode(bytes));
        }

        GrayImage[] phases = assets
            .Where(pair => pair.Key.StartsWith("phase_", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToArray();
        if (assets.Values.Any(image => image.Width != manifest.CanonicalWidth || image.Height != manifest.CanonicalHeight))
        {
            throw new InvalidDataException("Visual profile asset dimensions do not match its manifest.");
        }
        VisualAnchorProfile profile = new(
            manifest.Definition,
            manifest.RevisionId,
            manifest.BuiltAtUtc,
            manifest.Strategy,
            manifest.ReferenceClientWidth,
            manifest.ReferenceClientHeight,
            manifest.SampleCount,
            manifest.ReferenceBoundsWidth,
            manifest.ReferenceBoundsHeight,
            Required(assets, "median"),
            Required(assets, "edge"),
            Required(assets, "gray_reliability"),
            Required(assets, "edge_reliability"),
            phases,
            manifest.Metrics);
        profile.Validate();
        return profile;
    }

    private static async Task<VisualProfileManifest> WriteAssetsAsync(
        string directory,
        VisualAnchorProfile profile,
        CancellationToken cancellationToken)
    {
        Dictionary<string, GrayImage> images = new(StringComparer.Ordinal)
        {
            ["median"] = profile.MedianTemplate,
            ["edge"] = profile.EdgeTemplate,
            ["gray_reliability"] = profile.GrayReliability,
            ["edge_reliability"] = profile.EdgeReliability,
        };
        for (int index = 0; index < profile.PhaseTemplates.Count; index++)
        {
            images[$"phase_{index:000}"] = profile.PhaseTemplates[index];
        }

        Dictionary<string, string> assets = new(StringComparer.Ordinal);
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach ((string key, GrayImage image) in images)
        {
            string fileName = key + ".pgm";
            byte[] bytes = PortableGraymap.Encode(image);
            await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes, cancellationToken)
                .ConfigureAwait(false);
            assets.Add(key, fileName);
            hashes.Add(key, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        return new VisualProfileManifest
        {
            Definition = profile.Definition,
            RevisionId = profile.RevisionId,
            BuiltAtUtc = profile.BuiltAtUtc,
            Strategy = profile.Strategy,
            ReferenceClientWidth = profile.ReferenceClientWidth,
            ReferenceClientHeight = profile.ReferenceClientHeight,
            SampleCount = profile.SampleCount,
            ReferenceBoundsWidth = profile.ReferenceBoundsWidth,
            ReferenceBoundsHeight = profile.ReferenceBoundsHeight,
            CanonicalWidth = profile.MedianTemplate.Width,
            CanonicalHeight = profile.MedianTemplate.Height,
            Metrics = profile.Metrics,
            Assets = assets,
            Sha256 = hashes,
        };
    }

    private static void ValidateManifest(VisualProfileManifest manifest)
    {
        if (manifest.Format != VisualProfileManifest.FormatIdentifier || manifest.Definition is null ||
            manifest.Metrics is null ||
            manifest.SchemaVersion != VisualProfileManifest.CurrentSchemaVersion ||
            manifest.CanonicalWidth < 1 || manifest.CanonicalHeight < 1 ||
            manifest.Assets is null || manifest.Sha256 is null || manifest.Assets.Count < 4 ||
            !new HashSet<string>(manifest.Assets.Keys, StringComparer.Ordinal)
                .SetEquals(manifest.Sha256.Keys))
        {
            throw new InvalidDataException("Visual profile manifest is invalid.");
        }

        manifest.Definition.Validate();
    }

    private static GrayImage Required(IReadOnlyDictionary<string, GrayImage> assets, string key) =>
        assets.TryGetValue(key, out GrayImage? image)
            ? image
            : throw new InvalidDataException($"Visual profile asset is missing: {key}");

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, DatasetJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException($"JSON document is empty: {path}");
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, true);
        await JsonSerializer.SerializeAsync(stream, value, DatasetJson.Options, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteJsonAsync(temporary, value, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record CurrentRevision(string Revision);
}

public sealed record LoadedVisualProfileRevision(
    VisualAnchorProfile Profile,
    string RevisionDirectory);
