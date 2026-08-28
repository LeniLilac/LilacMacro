using LilacMacro.Core.Placements;

namespace LilacMacro.App.Runtime;

internal sealed class ChallengePlacementResolver(PlacementSetupStore store)
{
    private static readonly string[] MapIds =
    [
        "story-school-grounds",
        "story-flower-forest",
        "story-rose-kingdom",
        "story-fairy-king-forest",
        "story-kings-tomb",
    ];

    public async Task<int> ResolveCommonTeamAsync(CancellationToken cancellationToken)
    {
        List<(string Map, int Team)> teams = [];
        foreach (string mapId in MapIds)
        {
            PlacementMapDefinition map = PlacementMapCatalog.Definitions.First(candidate => candidate.Id == mapId);
            PlacementSetupDocument document;
            try
            {
                document = await store.LoadAsync(mapId, cancellationToken);
            }
            catch (FileNotFoundException error)
            {
                throw new InvalidDataException(
                    $"Configure {map.DisplayName} / Challenge in Setup. Challenge requires a placement setup for every possible random map.",
                    error);
            }
            PlacementRouteDefinition definition = PlacementRouteCatalog.For(map)
                .FirstOrDefault(candidate => candidate.Id == "challenge")
                ?? throw new InvalidDataException($"{map.DisplayName} has no Challenge route.");
            PlacementRouteSetup route = PlacementRouteCatalog.EffectiveRoute(document, definition);
            teams.Add((map.DisplayName, route.TeamSlot));
        }

        int[] distinct = teams.Select(item => item.Team).Distinct().ToArray();
        if (distinct.Length != 1)
        {
            string detail = string.Join(", ", teams.Select(item => $"{item.Map}=Team {item.Team}"));
            throw new InvalidDataException(
                $"Challenge routes must use one common team because the random map is revealed after team selection: {detail}.");
        }
        return distinct[0];
    }
}
