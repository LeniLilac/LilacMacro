using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

internal static class BundledPlacementMapCatalog
{
    internal const int ImageWidth = 1366;
    internal const int ImageHeight = 700;

    private static readonly IReadOnlyDictionary<string, int> ViewCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["story-school-grounds"] = 2,
            ["story-flower-forest"] = 2,
            ["story-rose-kingdom"] = 2,
            ["story-fairy-king-forest"] = 2,
            ["story-kings-tomb"] = 4,
            ["story-east-town"] = 2,
            ["raid-spirit-city-act-1"] = 2,
            ["raid-spirit-city-act-2"] = 2,
            ["raid-spirit-city-act-3"] = 2,
            ["expedition-school-grounds"] = 1,
            ["expedition-flower-forest"] = 1,
            ["expedition-rose-kingdom"] = 1,
            ["expedition-east-town"] = 1,
            ["event-villain-invasion-act-1"] = 2,
            ["event-villain-invasion-act-2"] = 2,
            ["event-villain-invasion-act-3"] = 2,
            ["event-villain-invasion-act-4"] = 2,
        };

    public static IReadOnlyList<PlacementMapReference> Discover(string applicationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        string assetRoot = Path.Combine(applicationRoot, "Assets", "PlacementMaps");
        List<PlacementMapReference> maps = [];
        foreach (PlacementMapDefinition definition in PlacementMapCatalog.Definitions)
        {
            if (!ViewCounts.TryGetValue(definition.Id, out int viewCount))
                throw new InvalidDataException($"Bundled map catalog is missing {definition.Id}.");
            string[] images = Enumerable.Range(1, viewCount)
                .Select(index => Path.Combine(assetRoot, $"{definition.Id}-{index}.jpg"))
                .ToArray();
            string? missing = images.FirstOrDefault(path => !File.Exists(path));
            if (missing is not null)
                throw new InvalidDataException($"Bundled map image is missing: {Path.GetFileName(missing)}.");
            maps.Add(new PlacementMapReference(definition, [], images, ImageWidth, ImageHeight));
        }
        return maps;
    }

    public static IReadOnlyList<PlacementMapReference> PreferLocal(
        IReadOnlyList<PlacementMapReference> bundled,
        IReadOnlyList<PlacementMapReference> local)
    {
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(local);
        Dictionary<string, PlacementMapReference> localById = local.ToDictionary(
            map => map.Definition.Id,
            StringComparer.Ordinal);
        return bundled
            .Select(map => localById.GetValueOrDefault(map.Definition.Id) ?? map)
            .ToArray();
    }
}
