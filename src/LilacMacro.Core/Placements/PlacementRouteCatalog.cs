namespace LilacMacro.Core.Placements;

public sealed record PlacementRouteDefinition(string Id, string Label, bool IsShared = false);

public static class PlacementRouteCatalog
{
    public const string SharedRouteId = "shared";

    private static readonly IReadOnlyList<PlacementRouteDefinition> StoryRoutes =
    [
        new(SharedRouteId, "SHARED", IsShared: true),
        new("act-1", "ACT 1"),
        new("act-2", "ACT 2"),
        new("act-3", "ACT 3"),
        new("act-4", "ACT 4"),
        new("act-5", "ACT 5"),
        new("infinite", "INFINITE"),
        new("mastery", "MASTERY"),
        new("challenge", "CHALLENGE"),
        new(LilacMacro.Core.Automation.TowerRunPolicy.TraitPlacementRouteId, "TRAIT TOWER"),
        new(LilacMacro.Core.Automation.TowerRunPolicy.TraitlessPlacementRouteId, "TRAITLESS TOWER"),
    ];

    public static IReadOnlyList<PlacementRouteDefinition> For(PlacementMapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Mode == PlacementMapMode.Story) return StoryRoutes;
        if (map.Mode == PlacementMapMode.Raid)
        {
            PlacementRouteDefinition exact = map.Id.EndsWith("act-2", StringComparison.OrdinalIgnoreCase)
                ? new("act-2", "ACT 2")
                : map.Id.EndsWith("act-3", StringComparison.OrdinalIgnoreCase)
                    ? new("act-3", "ACT 3")
                    : new("act-1", "ACT 1");
            return [new(SharedRouteId, "SHARED", IsShared: true), exact];
        }

        return [new(SharedRouteId, "DEFAULT", IsShared: true)];
    }

    public static PlacementRouteSetup EffectiveRoute(
        PlacementSetupDocument document,
        PlacementRouteDefinition route) =>
        route.IsShared || !document.Overrides.TryGetValue(route.Id, out PlacementRouteSetup? setup)
            ? document.Shared
            : setup;

    public static bool UsesShared(PlacementSetupDocument document, PlacementRouteDefinition route) =>
        !route.IsShared && !document.Overrides.ContainsKey(route.Id);
}
