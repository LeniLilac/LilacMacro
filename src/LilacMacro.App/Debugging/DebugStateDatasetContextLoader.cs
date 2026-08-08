using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal sealed record DebugStateDatasetContext(
    PixelRect RegionOfInterest,
    IReadOnlyList<DebugVisualAnchorIntent> VisualAnchors);

internal sealed class DebugStateDatasetContextLoader
{
    private readonly DatasetStore _datasets = new();

    public async Task<DebugStateDatasetContext> LoadAsync(
        DebugStateSpec state,
        CancellationToken cancellationToken)
    {
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

        PixelRect? region = null;
        foreach (int frameNumber in state.RegionFrames)
        {
            int index = frameNumber - 1;
            if (index < 0 || index >= dataset.Manifest.Frames.Count)
            {
                throw new InvalidDataException($"{state.Name} ROI frame {frameNumber} is missing.");
            }
            PixelRect bounds = dataset.Manifest.Frames[index].Annotations.FirstOrDefault()?.Bounds
                ?? throw new InvalidDataException(
                    $"{state.Name} ROI annotation is missing on frame {frameNumber}.");
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
}
