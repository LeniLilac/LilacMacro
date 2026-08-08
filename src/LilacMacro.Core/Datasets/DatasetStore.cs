using System.Security.Cryptography;
using System.Text.Json;
using LilacMacro.Core.Capture;
using LilacMacro.Core.Ocr;

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
        plan.Validate();

        return await CreateDraftCoreAsync(
            rootDirectory,
            plan.TargetSize,
            DatasetCaptureMode.Timed,
            plan.FrameCount,
            plan.Duration.TotalSeconds,
            windowTitle,
            processId,
            createdAtUtc,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<DatasetLocation> CreateManualDraftAsync(
        string rootDirectory,
        Geometry.PixelSize targetSize,
        string windowTitle,
        int processId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default) => CreateDraftCoreAsync(
            rootDirectory,
            Geometry.PixelSize.Create(targetSize.Width, targetSize.Height),
            DatasetCaptureMode.Manual,
            0,
            0,
            windowTitle,
            processId,
            createdAtUtc,
            cancellationToken);

    private async Task<DatasetLocation> CreateDraftCoreAsync(
        string rootDirectory,
        Geometry.PixelSize targetSize,
        DatasetCaptureMode captureMode,
        int requestedFrameCount,
        double requestedDurationSeconds,
        string windowTitle,
        int processId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);

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
            ClientWidth = targetSize.Width,
            ClientHeight = targetSize.Height,
            CaptureMode = captureMode,
            RequestedFrameCount = requestedFrameCount,
            RequestedDurationSeconds = requestedDurationSeconds,
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
        AnnotationScopePolicy.AddMembersToNewFrame(location.Manifest, frame);
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
        if (manifest.CaptureMode is not (DatasetCaptureMode.Timed or DatasetCaptureMode.Manual) ||
            manifest.CaptureMode == DatasetCaptureMode.Timed && manifest.RequestedFrameCount < 1 ||
            manifest.CaptureMode == DatasetCaptureMode.Manual &&
            (manifest.RequestedFrameCount != 0 || manifest.RequestedDurationSeconds != 0))
        {
            throw new InvalidDataException("Dataset capture mode metadata is invalid.");
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
        ValidateGlobalAnnotations(manifest);
        ValidateEvidenceRules(manifest);
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
            region.RecognitionConfidence is < 0 or > 1 ||
            region.MatchMode is not (OcrMatchMode.Exact or OcrMatchMode.FuzzyPhrase) ||
            region.EvidenceRole is not (OcrEvidenceRole.None or OcrEvidenceRole.Required or OcrEvidenceRole.Pool) ||
            region.SpatialSelector is not (
                OcrSpatialSelector.Any or
                OcrSpatialSelector.Leftmost or
                OcrSpatialSelector.Rightmost or
                OcrSpatialSelector.Topmost or
                OcrSpatialSelector.Bottommost or
                OcrSpatialSelector.SameRow or
                OcrSpatialSelector.NearestAnchor) ||
            (region.SpatialSelector is OcrSpatialSelector.SameRow or OcrSpatialSelector.NearestAnchor) &&
            string.IsNullOrWhiteSpace(region.SpatialAnchorText));
    }

    private static void ValidateGlobalAnnotations(DatasetManifest manifest)
    {
        foreach (IGrouping<Guid, BoxAnnotation> group in manifest.Frames
                     .SelectMany(frame => frame.Annotations)
                     .Where(annotation => annotation.GlobalGroupId.HasValue)
                     .GroupBy(annotation => annotation.GlobalGroupId!.Value))
        {
            BoxAnnotation first = group.First();
            bool exactlyOnePerFrame = manifest.Frames.All(frame =>
                frame.Annotations.Count(annotation => annotation.GlobalGroupId == group.Key) == 1);
            bool sharedFieldsMatch = group.All(annotation =>
                annotation.Bounds == first.Bounds &&
                string.Equals(annotation.Label, first.Label, StringComparison.Ordinal) &&
                string.Equals(annotation.Notes, first.Notes, StringComparison.Ordinal) &&
                annotation.MinimumPoolMatches == first.MinimumPoolMatches);
            if (!exactlyOnePerFrame || group.Count() != manifest.Frames.Count || !sharedFieldsMatch)
            {
                throw new InvalidDataException("Global annotations must have one matching member on every frame.");
            }
        }
    }

    private static void ValidateEvidenceRules(DatasetManifest manifest)
    {
        foreach (BoxAnnotation annotation in manifest.Frames
                     .SelectMany(frame => frame.Annotations)
                     .Where(annotation => annotation.GlobalGroupId is null))
        {
            if (!OcrEvidenceRulePolicy.IsValid(annotation.MinimumPoolMatches, Regions(annotation)))
            {
                throw new InvalidDataException("Dataset contains an invalid OCR evidence rule.");
            }
        }

        foreach (IGrouping<Guid, BoxAnnotation> group in manifest.Frames
                     .SelectMany(frame => frame.Annotations)
                     .Where(annotation => annotation.GlobalGroupId.HasValue)
                     .GroupBy(annotation => annotation.GlobalGroupId!.Value))
        {
            if (!OcrEvidenceRulePolicy.IsValid(group.First().MinimumPoolMatches, group.SelectMany(Regions)))
            {
                throw new InvalidDataException("Dataset contains an invalid global OCR evidence rule.");
            }
        }
    }

    private static IEnumerable<OcrTextRegion> Regions(BoxAnnotation annotation) => annotation.OcrTrials
        .SelectMany(trial => trial.Regions);
}
