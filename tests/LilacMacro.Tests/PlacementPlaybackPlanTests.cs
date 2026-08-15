using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementPlaybackPlanTests
{
    [Fact]
    public void CreateSplitsTimelineAtStartBoundary()
    {
        PlacementStep first = PlacementStep.CreatePlace(1, 10, 20, PlacementTargetingPriority.First, PlacementAutoUpgradePriority.Off);
        PlacementStep after = PlacementStep.CreatePlace(2, 30, 40, PlacementTargetingPriority.First, PlacementAutoUpgradePriority.Off);
        PlacementRouteSetup route = new() { RouteId = "shared", Steps = [first, PlacementStep.CreateStartGame(), after] };

        PlacementPlaybackPlan plan = PlacementPlaybackPlan.Create(route);

        Assert.Equal(first.Id, Assert.Single(plan.BeforeStart).Id);
        Assert.Equal(after.Id, Assert.Single(plan.AfterStart).Id);
        Assert.Equal(PlacementStepKind.StartGame, plan.StartGame.Kind);
    }

    [Fact]
    public void GroupBatchesOnlyContiguousPlacements()
    {
        PlacementStep first = Place(1);
        PlacementStep second = Place(2);
        PlacementStep delay = new() { Kind = PlacementStepKind.Delay, DelayDurationMilliseconds = 10 };
        PlacementStep third = Place(3);

        IReadOnlyList<PlacementPlaybackGroup> groups = PlacementPlaybackPlan.Group([first, second, delay, third]);

        Assert.Collection(
            groups,
            group => Assert.Equal(2, group.Steps.Count),
            group => Assert.Equal(PlacementStepKind.Delay, group.Kind),
            group => Assert.Single(group.Steps));
    }

    private static PlacementStep Place(int slot) => PlacementStep.CreatePlace(
        slot, slot * 10, slot * 20, PlacementTargetingPriority.First, PlacementAutoUpgradePriority.Off);
}
