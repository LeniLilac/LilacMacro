using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Placements;

public sealed class PlacementMapCatalog
{
    private readonly DatasetStore _datasetStore;

    public PlacementMapCatalog(DatasetStore? datasetStore = null)
    {
        _datasetStore = datasetStore ?? new DatasetStore();
    }

    public static IReadOnlyList<PlacementMapDefinition> Definitions { get; } =
    [
        new("story-school-grounds", PlacementMapMode.Story, "School Grounds", ["Story School Grounds Map"]),
        new("story-flower-forest", PlacementMapMode.Story, "Flower Forest", ["Story Flower Forest Map"]),
        new("story-rose-kingdom", PlacementMapMode.Story, "Rose Kingdom", ["Story Rose Kingdom Map"]),
        new("story-fairy-king-forest", PlacementMapMode.Story, "Fairy King Forest", ["Story Fairy King Forest"]),
        new(
            "story-kings-tomb",
            PlacementMapMode.Story,
            "King's Tomb",
            ["Story King's Tomb Map Angle 1", "Story King's Tomb Map Angle 2"]),
        new("story-east-town", PlacementMapMode.Story, "East Town", ["East Town Story Map Views"]),
        new("raid-spirit-city-act-1", PlacementMapMode.Raid, "Spirit City · Act 1", ["Raid Spirit City Act 1 Map"]),
        new("raid-spirit-city-act-2", PlacementMapMode.Raid, "Spirit City · Act 2", ["Raid Spirit City Act 2 Map"]),
        new("raid-spirit-city-act-3", PlacementMapMode.Raid, "Spirit City · Act 3", ["Raid Spirit City Act 3 Map"]),
        new("expedition-school-grounds", PlacementMapMode.Expedition, "School Grounds", ["Expedition School Ground Map"]),
        new("expedition-flower-forest", PlacementMapMode.Expedition, "Flower Forest", ["Expedition Flower Forest Map"]),
        new("expedition-rose-kingdom", PlacementMapMode.Expedition, "Rose Kingdom", ["Expedition Rose Kingdom Map"]),
        new("expedition-east-town", PlacementMapMode.Expedition, "East Town", ["Expedition East Town Map Preview"]),
        new("event-villain-invasion-act-1", PlacementMapMode.Events, "Villain Invasion · Act 1", ["Event Act 1 Map Image"]),
        new("event-villain-invasion-act-2", PlacementMapMode.Events, "Villain Invasion · Act 2", ["event act 2 map image"]),
        new("event-villain-invasion-act-3", PlacementMapMode.Events, "Villain Invasion · Act 3", ["event act 3 map image"]),
        new("event-villain-invasion-act-4", PlacementMapMode.Events, "Villain Invasion · Act 4", ["event act 4 map img"]),
    ];

    public async Task<IReadOnlyList<PlacementMapReference>> DiscoverAsync(
        string datasetRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        IReadOnlyList<DatasetLocation> datasets = await _datasetStore
            .DiscoverAsync(datasetRoot, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, DatasetLocation> latestByName = datasets
            .Where(dataset => dataset.Manifest.IsFinalized)
            .GroupBy(dataset => dataset.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(dataset => dataset.Manifest.CreatedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        List<PlacementMapReference> references = [];
        foreach (PlacementMapDefinition definition in Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DatasetLocation[] matchingDatasets = definition.DatasetNames
                .Select(name => latestByName.GetValueOrDefault(name))
                .Where(dataset => dataset is not null)
                .Cast<DatasetLocation>()
                .ToArray();
            if (matchingDatasets.Length == 0) continue;

            DatasetLocation primary = matchingDatasets[0];
            DatasetLocation[] compatibleDatasets = matchingDatasets
                .Where(dataset =>
                    dataset.Manifest.ClientWidth == primary.Manifest.ClientWidth &&
                    dataset.Manifest.ClientHeight == primary.Manifest.ClientHeight)
                .ToArray();
            string[] images = compatibleDatasets
                .SelectMany(dataset => dataset.Manifest.Frames.Select(frame =>
                    Path.Combine(dataset.ImagesPath, frame.FileName)))
                .Where(File.Exists)
                .ToArray();
            if (images.Length == 0) continue;

            references.Add(new PlacementMapReference(
                definition,
                compatibleDatasets.Select(dataset => dataset.DirectoryPath).ToArray(),
                images,
                primary.Manifest.ClientWidth,
                primary.Manifest.ClientHeight));
        }

        return references;
    }
}
