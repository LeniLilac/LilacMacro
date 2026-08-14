namespace LilacMacro.Core.Automation;

public readonly record struct MapPreparationStep(int VirtualKey, int HoldMilliseconds);

public static class MapPreparationPolicy
{
    public const string ExpeditionShop = "utility-expedition-shop";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<MapPreparationStep>> Plans =
        new Dictionary<string, IReadOnlyList<MapPreparationStep>>(StringComparer.OrdinalIgnoreCase)
        {
            ["event-villain-invasion-act-1"] =
            [
                new(0x57, 750),
                new(0x44, 750),
                new(0x57, 2200),
            ],
            ["event-villain-invasion-act-2"] =
            [
                new(0x57, 1000),
                new(0x41, 100),
                new(0x57, 1300),
            ],
            ["event-villain-invasion-act-3"] =
            [
                new(0x53, 900),
                new(0x44, 750),
            ],
            [ExpeditionShop] =
            [
                new(0x57, 2500),
                new(0x44, 750),
                new(0x57, 3300),
                new(0x44, 500),
            ],
        };

    public static IReadOnlyList<MapPreparationStep> For(string mapId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        return Plans.GetValueOrDefault(mapId) ?? [];
    }
}
