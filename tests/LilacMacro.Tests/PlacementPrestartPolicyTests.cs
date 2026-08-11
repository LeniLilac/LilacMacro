using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementPrestartPolicyTests
{
    [Fact]
    public void GuaranteedDelayAtThirtySecondsIsAccepted()
    {
        PlacementRouteSetup route = RouteWith(
            new PlacementStep
            {
                Kind = PlacementStepKind.Delay,
                DelayDurationMilliseconds = 29_000,
                DelayAfterMilliseconds = 1_000,
            },
            PlacementStep.CreateStartGame());

        PlacementSetupRules.ValidateRoute(route, 1366, 700);

        Assert.Equal(30_000, PlacementPrestartPolicy.CalculateGuaranteedDelayMilliseconds(route));
    }

    [Fact]
    public void GuaranteedDelayAboveThirtySecondsIsRejected()
    {
        PlacementRouteSetup route = RouteWith(
            new PlacementStep
            {
                Kind = PlacementStepKind.Delay,
                DelayDurationMilliseconds = 30_000,
                DelayAfterMilliseconds = 1,
            },
            PlacementStep.CreateStartGame());

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            PlacementSetupRules.ValidateRoute(route, 1366, 700));

        Assert.Contains("Guaranteed prestart delays", error.Message, StringComparison.Ordinal);
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

        Assert.Equal(0, PlacementPrestartPolicy.CalculateGuaranteedDelayMilliseconds(route));
    }

    [Theory]
    [InlineData(0, false, MatchStartBoundaryDecision.ClickStart)]
    [InlineData(1, true, MatchStartBoundaryDecision.Indeterminate)]
    [InlineData(3, false, MatchStartBoundaryDecision.Indeterminate)]
    [InlineData(3, true, MatchStartBoundaryDecision.AutoStarted)]
    public void BoundaryRequiresStableAbsenceAndPositiveRuntimeEvidence(
        int misses,
        bool runtimeEvidence,
        MatchStartBoundaryDecision expected)
    {
        Assert.Equal(expected, PlacementPrestartPolicy.DecideBoundary(misses, runtimeEvidence));
    }

    private static PlacementRouteSetup RouteWith(params PlacementStep[] steps) => new()
    {
        RouteId = PlacementRouteCatalog.SharedRouteId,
        Steps = [.. steps],
    };
}
