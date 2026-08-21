using LilacMacro.Core.Automation;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Runtime;

internal sealed class TowerPlacementResolver(PlacementSetupStore store)
{
    public async Task<int> ResolveTeamAsync(
        string mapName,
        TowerType type,
        CancellationToken cancellationToken)
    {
        PlacementMapDefinition map = PlacementMapCatalog.Definitions.FirstOrDefault(candidate =>
            candidate.Mode == PlacementMapMode.Story &&
            string.Equals(candidate.DisplayName, mapName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Tower returned unsupported Story map: {mapName}.");
        PlacementSetupDocument document = await store.LoadAsync(map.Id, cancellationToken).ConfigureAwait(false);
        PlacementSetupRules.Validate(document);
        PlacementRouteDefinition route = PlacementRouteCatalog.For(map).First(candidate =>
            candidate.Id == TowerRunPolicy.PlacementRouteId(type));
        return PlacementRouteCatalog.EffectiveRoute(document, route).TeamSlot;
    }

    public async Task ValidateAsync(TowerType type, CancellationToken cancellationToken)
    {
        foreach (PlacementMapDefinition map in PlacementMapCatalog.Definitions.Where(candidate =>
                     candidate.Mode == PlacementMapMode.Story))
        {
            _ = await ResolveTeamAsync(map.DisplayName, type, cancellationToken).ConfigureAwait(false);
        }
    }
}
