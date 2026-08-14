using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementSetupTimingTests
{
    [Fact]
    public void AuthoredDelayBeyondTheRetiredAutoStartWindowIsAccepted()
    {
        PlacementRouteSetup route = RouteWith(
            new PlacementStep
            {
                Kind = PlacementStepKind.Delay,
                DelayDurationMilliseconds = 120_000,
                DelayAfterMilliseconds = 60_000,
            },
            PlacementStep.CreateStartGame());

        PlacementSetupRules.ValidateRoute(route, 1366, 700);
    }

    [Fact]
    public void DelaysAfterStartDoNotConsumePrestartBudget()
    {
        PlacementRouteSetup route = RouteWith(
            PlacementStep.CreateStartGame(),
            new PlacementStep
            {
                Kind = PlacementStepKind.Delay,
                DelayDurationMilliseconds = 60_000,
                DelayAfterMilliseconds = 60_000,
            });

        PlacementSetupRules.ValidateRoute(route, 1366, 700);
    }

    private static PlacementRouteSetup RouteWith(params PlacementStep[] steps) => new()
    {
        RouteId = PlacementRouteCatalog.SharedRouteId,
        Steps = [.. steps],
    };
}
