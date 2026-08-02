using System.Security.Cryptography;
using System.Text.Json;
using LilacMacro.Core.Capture;

namespace LilacMacro.Core.Datasets;

public sealed class DatasetStore
{
    public const string ManifestFileName = "dataset.json";
    public const string ImagesDirectoryName = "images";

    public async Task<DatasetLocation> CreateDraftAsync(
        string rootDirectory,
        CapturePlan plan,
        string windowTitle,
        int processId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);
        plan.Validate();

        Directory.CreateDirectory(rootDirectory);
        Guid id = Guid.NewGuid();
        string draftName = $".draft-{createdAtUtc:yyyyMMdd-HHmmss}-{id:N}"[..39];
        string directory = Path.Combine(Path.GetFullPath(rootDirectory), draftName);
        Directory.CreateDirectory(Path.Combine(directory, ImagesDirectoryName));

        DatasetManifest manifest = new()
        {
            Id = id,
            CreatedAtUtc = createdAtUtc,
            SourceWindowTitle = windowTitle,
            SourceProcessId = processId,
            ClientWidth = plan.TargetSize.Width,
            ClientHeight = plan.TargetSize.Height,
            RequestedFrameCount = plan.FrameCount,
            RequestedDurationSeconds = plan.Duration.TotalSeconds,
        };
        DatasetLocation location = new(directory, manifest);
        await SaveAsync(location, cancellationToken).ConfigureAwait(false);
        return location;
    }

    public async Task<DatasetFrame> AddFrameAsync(
        DatasetLocation location,
        ReadOnlyMemory<byte> pngBytes,
        int width,
        int height,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (width != location.Manifest.ClientWidth || height != location.Manifest.ClientHeight)
        {
            throw new InvalidOperationException(
                $"Captured frame is {width} × {height}; dataset requires {location.Manifest.ClientWidth} × {location.Manifest.ClientHeight}.");
        }

        int next = location.Manifest.Frames.Count + 1;
        string fileName = $"frame-{next:0000}.png";
        string imagesDirectory = location.ImagesPath;
        Directory.CreateDirectory(imagesDirectory);
        string destination = Path.Combine(imagesDirectory, fileName);
        if (File.Exists(destination)) throw new IOException($"Capture file already exists: {destination}");

        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, pngBytes.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        string sha256 = Convert.ToHexString(SHA256.HashData(pngBytes.Span)).ToLowerInvariant();
        DatasetFrame frame = new()
        {
            FileName = fileName,
            CapturedAtUtc = capturedAtUtc,
            Sha256 = sha256,
            Width = width,
            Height = height,
        };
        location.Manifest.Frames.Add(frame);
        await SaveAsync(location, cancellationToken).ConfigureAwait(false);
        return frame;
    }

    public async Task<DatasetLocation> LoadAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string manifestPath = Path.Combine(fullDirectory, ManifestFileName);
        await using FileStream stream = File.OpenRead(manifestPath);
        DatasetManifest manifest = await JsonSerializer.DeserializeAsync<DatasetManifest>(
            stream,
            DatasetJson.Options,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Dataset manifest is empty: {manifestPath}");
        ValidateManifest(manifest, fullDirectory);
        return new DatasetLocation(fullDirectory, manifest);
    }

    public async Task SaveAsync(DatasetLocation location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ValidateManifest(location.Manifest, location.DirectoryPath);
        Directory.CreateDirectory(location.DirectoryPath);
        string destination = location.ManifestPath;
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    location.Manifest,
                    DatasetJson.Options,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<DatasetLocation> FinalizeAsync(
        DatasetLocation draft,
        string name,
        string notes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Manifest.IsFinalized) return draft;
        if (draft.Manifest.Frames.Count == 0) throw new InvalidOperationException("A dataset must contain at least one frame.");

        draft.Manifest.Name = DatasetNaming.ValidateDisplayName(name);
        draft.Manifest.Notes = notes.Trim();
        await SaveAsync(draft, cancellationToken).ConfigureAwait(false);

        string root = Directory.GetParent(draft.DirectoryPath)?.FullName
            ?? throw new InvalidOperationException("Dataset draft has no parent directory.");
        string baseName = $"{DatasetNaming.Slugify(draft.Manifest.Name)}-{draft.Manifest.CreatedAtUtc:yyyyMMdd-HHmmss}";
        string destination = FindAvailableDirectory(root, baseName);
        Directory.Move(draft.DirectoryPath, destination);

        draft.Manifest.IsFinalized = true;
        DatasetLocation finalized = new(destination, draft.Manifest);
        await SaveAsync(finalized, cancellationToken).ConfigureAwait(false);
        return finalized;
    }

    public async Task<IReadOnlyList<DatasetLocation>> DiscoverAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory)) return [];

        List<DatasetLocation> datasets = [];
        foreach (string directory in Directory.EnumerateDirectories(rootDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Combine(directory, ManifestFileName))) continue;
            try
            {
                datasets.Add(await LoadAsync(directory, cancellationToken).ConfigureAwait(false));
            }
            catch (IOException)
            {
                // A partially written or externally locked directory stays untouched.
            }
            catch (JsonException)
            {
                // Invalid datasets are not silently rewritten by discovery.
            }
        }

        return datasets
            .OrderByDescending(dataset => dataset.Manifest.CreatedAtUtc)
            .ToArray();
    }

    private static string FindAvailableDirectory(string root, string baseName)
    {
        for (int suffix = 0; suffix < 10_000; suffix++)
        {
            string name = suffix == 0 ? baseName : $"{baseName}-{suffix + 1}";
            string candidate = Path.Combine(root, name);
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not allocate a unique dataset directory name.");
    }

    private static void ValidateManifest(DatasetManifest manifest, string directory)
    {
        if (!string.Equals(manifest.Format, DatasetManifest.FormatIdentifier, StringComparison.Ordinal) ||
            !string.Equals(manifest.CoordinateSpace, "roblox_client_pixels_half_open", StringComparison.Ordinal) ||
            !string.Equals(manifest.ImageRoot, ImagesDirectoryName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Dataset format metadata is invalid in {directory}.");
        }
        if (manifest.SchemaVersion != DatasetManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported dataset schema {manifest.SchemaVersion} in {directory}.");
        }
        if (manifest.ClientWidth <= 0 || manifest.ClientHeight <= 0)
        {
            throw new InvalidDataException("Dataset client dimensions must be positive.");
        }
        if (manifest.Frames.Any(frame =>
                Path.GetFileName(frame.FileName) != frame.FileName ||
                !frame.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                frame.Width != manifest.ClientWidth ||
                frame.Height != manifest.ClientHeight ||
                frame.Annotations.Any(annotation => IsInvalid(annotation, frame))))
        {
            throw new InvalidDataException("Dataset contains a frame or annotation outside its declared client geometry.");
        }
    }

    private static bool IsInvalid(BoxAnnotation annotation, DatasetFrame frame)
    {
        Geometry.PixelSize frameSize = new(frame.Width, frame.Height);
        return !annotation.Bounds.IsInside(frameSize) || annotation.OcrTrials.Any(trial => IsInvalid(trial, annotation));
    }

    private static bool IsInvalid(OcrTrial trial, BoxAnnotation annotation)
    {
        if (trial.Confidence is < 0 or > 1 ||
            trial.ModelLoadMilliseconds < 0 ||
            trial.InferenceMilliseconds < 0 ||
            trial.ModelName is not ("PP-OCRv6_small_rec" or "PP-OCRv6_tiny_rec") ||
            trial.DetectorModelName is not ("" or "PP-OCRv6_small_det" or "PP-OCRv6_tiny_det") ||
            trial.Device is not ("cpu" or "gpu:0"))
        {
            return true;
        }

        return trial.Regions.Any(region =>
            !annotation.Bounds.Contains(region.Bounds) ||
            region.DetectionConfidence is < 0 or > 1 ||
            region.RecognitionConfidence is < 0 or > 1);
    }
}
