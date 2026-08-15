using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.LocalSession;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilacMacro.App.Debugging;

internal sealed record DebugStateDatasetContext(
    PixelRect RegionOfInterest,
    IReadOnlyList<DebugVisualAnchorIntent> VisualAnchors);

internal sealed class DebugStateDatasetContextLoader
{
    private const string EmbeddedContextResource = "LilacMacro.App.RuntimeStateContexts.json";
    private static readonly Lazy<RuntimeContextCatalog> EmbeddedContexts = new(LoadEmbeddedContexts);
    private readonly DatasetStore _datasets = new();

    public async Task<DebugStateDatasetContext> LoadAsync(
        DebugStateSpec state,
        CancellationToken cancellationToken)
    {
        string? overridePath = Environment.GetEnvironmentVariable("LILACMACRO_RUNNER_STATE_CONTEXTS");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            RunnerStateContextSnapshot[] contexts = await ReadOverridesAsync(overridePath, cancellationToken);
            RunnerStateContextSnapshot context = contexts.FirstOrDefault(candidate =>
                string.Equals(candidate.State, state.Name, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Runner snapshot has no context for {state.Name}.");
            return new DebugStateDatasetContext(
                context.RegionOfInterest,
                context.VisualAnchors.Select(anchor => new DebugVisualAnchorIntent(
                    anchor.Text,
                    anchor.MatchMode,
                    anchor.SpatialSelector,
                    anchor.SpatialAnchorText)).ToArray());
        }

        if (!Directory.Exists(state.DatasetDirectory) || IsInstalledDatasetPath(state.DatasetDirectory))
        {
            return LoadEmbeddedContext(state);
        }

        DatasetLocation dataset = await _datasets.LoadAsync(state.DatasetDirectory, cancellationToken);
        PixelSize datasetSize = new(dataset.Manifest.ClientWidth, dataset.Manifest.ClientHeight);
        if (datasetSize != DebugWorkflowCatalog.ClientSize)
        {
            throw new InvalidDataException(
                $"{state.Name} ROI dataset is {datasetSize}; expected {DebugWorkflowCatalog.ClientSize}.");
        }
        if (state.RegionFrames.Count == 0)
        {
            throw new InvalidDataException($"{state.Name} has no ROI frames.");
        }
        if (string.IsNullOrWhiteSpace(state.RegionLabel))
        {
            throw new InvalidDataException($"{state.Name} has no explicit ROI annotation label.");
        }

        PixelRect? region = null;
        foreach (int frameNumber in state.RegionFrames)
        {
            int index = frameNumber - 1;
            if (index < 0 || index >= dataset.Manifest.Frames.Count)
            {
                throw new InvalidDataException($"{state.Name} ROI frame {frameNumber} is missing.");
            }
            BoxAnnotation[] annotations = dataset.Manifest.Frames[index].Annotations
                .Where(candidate => string.Equals(
                    candidate.Label, state.RegionLabel, StringComparison.Ordinal))
                .ToArray();
            if (annotations.Length != 1)
            {
                throw new InvalidDataException(
                    $"{state.Name} requires exactly one ROI annotation '{state.RegionLabel}' " +
                    $"on frame {frameNumber}; found {annotations.Length}.");
            }
            PixelRect bounds = annotations[0].Bounds;
            region = region is null ? bounds : PixelRect.Union(region.Value, bounds);
        }

        DebugVisualAnchorIntent[] visualAnchors = dataset.Manifest.Frames
            .SelectMany(frame => frame.Annotations)
            .SelectMany(annotation => annotation.OcrTrials)
            .SelectMany(trial => trial.Regions)
            .Where(candidate => candidate.IsVisualAnchor && !string.IsNullOrWhiteSpace(candidate.Text))
            .GroupBy(candidate => (
                Text: OcrRuleEngine.Normalize(candidate.Text),
                candidate.MatchMode,
                candidate.SpatialSelector,
                Anchor: OcrRuleEngine.Normalize(candidate.SpatialAnchorText)))
            .Select(group => group.First())
            .Select(candidate => new DebugVisualAnchorIntent(
                candidate.Text.Trim(),
                candidate.MatchMode,
                candidate.SpatialSelector,
                candidate.SpatialAnchorText))
            .ToArray();
        return new DebugStateDatasetContext(
            region ?? throw new InvalidDataException($"{state.Name} has no ROI."),
            visualAnchors);
    }

    private static DebugStateDatasetContext LoadEmbeddedContext(DebugStateSpec state)
    {
        string datasetName = Path.GetFileName(state.DatasetDirectory);
        RuntimeContextDataset dataset = EmbeddedContexts.Value.Datasets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, datasetName, StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                $"Embedded runtime context catalog has no dataset named {datasetName}.");
        PixelSize datasetSize = new(dataset.ClientWidth, dataset.ClientHeight);
        if (datasetSize != DebugWorkflowCatalog.ClientSize)
        {
            throw new InvalidDataException(
                $"{state.Name} ROI dataset is {datasetSize}; expected {DebugWorkflowCatalog.ClientSize}.");
        }
        if (state.RegionFrames.Count == 0)
            throw new InvalidDataException($"{state.Name} has no ROI frames.");
        if (string.IsNullOrWhiteSpace(state.RegionLabel))
            throw new InvalidDataException($"{state.Name} has no explicit ROI annotation label.");

        PixelRect? region = null;
        foreach (int frameNumber in state.RegionFrames)
        {
            if (frameNumber < 1 || frameNumber > dataset.FrameCount)
                throw new InvalidDataException($"{state.Name} ROI frame {frameNumber} is missing.");
            RuntimeContextAnnotation[] annotations = dataset.Annotations
                .Where(candidate => candidate.Frame == frameNumber && string.Equals(
                    candidate.Label, state.RegionLabel, StringComparison.Ordinal))
                .ToArray();
            if (annotations.Length != 1)
            {
                throw new InvalidDataException(
                    $"{state.Name} requires exactly one ROI annotation '{state.RegionLabel}' " +
                    $"on frame {frameNumber}; found {annotations.Length}.");
            }
            region = region is null
                ? annotations[0].Bounds
                : PixelRect.Union(region.Value, annotations[0].Bounds);
        }

        DebugVisualAnchorIntent[] visualAnchors = dataset.VisualAnchors
            .Select(anchor => new DebugVisualAnchorIntent(
                anchor.Text,
                anchor.MatchMode ?? OcrMatchMode.Exact,
                anchor.SpatialSelector ?? OcrSpatialSelector.Any,
                anchor.SpatialAnchorText))
            .ToArray();
        return new DebugStateDatasetContext(
            region ?? throw new InvalidDataException($"{state.Name} has no ROI."),
            visualAnchors);
    }

    private static bool IsInstalledDatasetPath(string path)
    {
        string installedRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "Assets", "RuntimeEvidence")) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(path) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeContextCatalog LoadEmbeddedContexts()
    {
        using Stream stream = typeof(DebugStateDatasetContextLoader).Assembly
            .GetManifestResourceStream(EmbeddedContextResource)
            ?? throw new InvalidDataException(
                $"Application resource {EmbeddedContextResource} is missing.");
        JsonSerializerOptions options = new();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        RuntimeContextCatalog catalog = JsonSerializer.Deserialize<RuntimeContextCatalog>(stream, options)
            ?? throw new InvalidDataException("Embedded runtime context catalog is empty.");
        if (catalog.SchemaVersion != 1)
            throw new InvalidDataException("Embedded runtime context catalog schema is unsupported.");
        return catalog;
    }

    private static async Task<RunnerStateContextSnapshot[]> ReadOverridesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(Path.GetFullPath(path));
        return await JsonSerializer.DeserializeAsync<RunnerStateContextSnapshot[]>(
            stream,
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Runner state contexts are empty.");
    }

    private sealed record RuntimeContextCatalog
    {
        public int SchemaVersion { get; init; }
        public RuntimeContextDataset[] Datasets { get; init; } = [];
    }

    private sealed record RuntimeContextDataset
    {
        public string Name { get; init; } = string.Empty;
        public int ClientWidth { get; init; }
        public int ClientHeight { get; init; }
        public int FrameCount { get; init; }
        public RuntimeContextAnnotation[] Annotations { get; init; } = [];
        public RuntimeContextVisualAnchor[] VisualAnchors { get; init; } = [];
    }

    private sealed record RuntimeContextAnnotation
    {
        public int Frame { get; init; }
        public string Label { get; init; } = string.Empty;
        public PixelRect Bounds { get; init; }
    }

    private sealed record RuntimeContextVisualAnchor
    {
        public string Text { get; init; } = string.Empty;
        public OcrMatchMode? MatchMode { get; init; }
        public OcrSpatialSelector? SpatialSelector { get; init; }
        public string? SpatialAnchorText { get; init; }
    }
}
