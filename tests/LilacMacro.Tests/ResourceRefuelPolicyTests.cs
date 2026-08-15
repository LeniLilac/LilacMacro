using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class ResourceRefuelPolicyTests
{
    [Fact]
    public void PlanCatalogOffersCombinedAndIndependentRefuelRoutes()
    {
        Assert.Equal(
            [
                ResourceRefuelPolicy.CombinedRoute,
                ResourceRefuelPolicy.GoldMineRoute,
                ResourceRefuelPolicy.ResourceDrillRoute,
            ],
            ResourceRefuelPolicy.Routes);
    }

    [Fact]
    public void RoutesKeepMineAndDrillAsIndependentTargets()
    {
        Assert.Equal(
            [ResourceRefuelTarget.GoldMine],
            ResourceRefuelPolicy.TargetsFor(ResourceRefuelPolicy.GoldMineRoute));
        Assert.Equal(
            [ResourceRefuelTarget.ResourceDrill],
            ResourceRefuelPolicy.TargetsFor(ResourceRefuelPolicy.ResourceDrillRoute));
        Assert.Equal(
            [ResourceRefuelTarget.GoldMine, ResourceRefuelTarget.ResourceDrill],
            ResourceRefuelPolicy.TargetsFor(ResourceRefuelPolicy.CombinedRoute));
    }

    [Fact]
    public void CombinedRouteUsesOneSharedIntervalAfterThePairCompletes()
    {
        DateTimeOffset completedAt = new(2026, 8, 14, 12, 30, 0, TimeSpan.Zero);

        Assert.Equal(
            completedAt.AddMinutes(400),
            UtilityTaskPolicy.NextDue(ResourceRefuelPolicy.CombinedRoute, completedAt, 400));
    }

    [Fact]
    public void WalkRoutesMatchFieldValidatedExpeditionsTimings()
    {
        Assert.Equal(
            [new ResourceRefuelWalkStep('W', 3000), new('A', 820), new('W', 2600)],
            ResourceRefuelPolicy.WalkFor(ResourceRefuelTarget.GoldMine));
        Assert.Equal(
            [new ResourceRefuelWalkStep('W', 3000), new('A', 750), new('W', 1000), new('A', 1600)],
            ResourceRefuelPolicy.WalkFor(ResourceRefuelTarget.ResourceDrill));
    }

    [Fact]
    public void StationOpenAttemptsUseTwoFourAndSixSecondObservations()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ResourceRefuelPolicy.StationObservationDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), ResourceRefuelPolicy.StationObservationDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(6), ResourceRefuelPolicy.StationObservationDelay(3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResourceRefuelPolicy.StationObservationDelay(4));
    }

    [Theory]
    [InlineData(648, 587, 76, 19, 517, 416, 69, 22, 786, 415, 60, 24, 901, 363)]
    [InlineData(654, 545, 65, 19, 544, 404, 59, 20, 769, 406, 49, 18, 863, 361)]
    [InlineData(657, 506, 54, 15, 573, 395, 46, 14, 752, 393, 39, 17, 827, 359)]
    public void DialogActionsDeriveQuantityAcrossRecordedUiScales(
        int addFuelX,
        int addFuelY,
        int addFuelWidth,
        int addFuelHeight,
        int confirmX,
        int confirmY,
        int confirmWidth,
        int confirmHeight,
        int cancelX,
        int cancelY,
        int cancelWidth,
        int cancelHeight,
        int expectedQuantityX,
        int expectedQuantityY)
    {
        PixelRect addFuel = new(addFuelX, addFuelY, addFuelWidth, addFuelHeight);
        PixelRect confirm = new(confirmX, confirmY, confirmWidth, confirmHeight);
        PixelRect cancel = new(cancelX, cancelY, cancelWidth, cancelHeight);

        bool accepted = ResourceRefuelPolicy.TryResolveDialogActions(
            addFuel,
            confirm,
            cancel,
            new PixelSize(1366, 700),
            out ResourceRefuelDialogActions actions);

        Assert.True(accepted);
        Assert.Equal(confirm.Center, actions.Confirm);
        Assert.Equal(new PixelPoint(expectedQuantityX, expectedQuantityY), actions.Quantity);
    }

    [Theory]
    [InlineData(400, 587, 76, 19, 517, 416, 69, 22, 786, 415, 60, 24)]
    [InlineData(648, 587, 76, 19, 515, 414, 71, 25, 600, 500, 62, 25)]
    [InlineData(648, 587, 76, 19, 515, 414, 71, 25, 1200, 414, 62, 25)]
    public void DialogActionsRejectUnsafeLayouts(
        int addFuelX,
        int addFuelY,
        int addFuelWidth,
        int addFuelHeight,
        int confirmX,
        int confirmY,
        int confirmWidth,
        int confirmHeight,
        int cancelX,
        int cancelY,
        int cancelWidth,
        int cancelHeight)
    {
        Assert.False(ResourceRefuelPolicy.TryResolveDialogActions(
            new PixelRect(addFuelX, addFuelY, addFuelWidth, addFuelHeight),
            new PixelRect(confirmX, confirmY, confirmWidth, confirmHeight),
            new PixelRect(cancelX, cancelY, cancelWidth, cancelHeight),
            new PixelSize(1366, 700),
            out _));
    }
}
