using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class ResourceRefuelPolicyTests
{
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
    public void DialogActionsDeriveQuantityFromFreshButtonGeometry()
    {
        PixelRect confirm = new(515, 414, 71, 25);
        PixelRect cancel = new(785, 414, 62, 25);

        bool accepted = ResourceRefuelPolicy.TryResolveDialogActions(
            confirm,
            cancel,
            new PixelSize(1366, 700),
            out ResourceRefuelDialogActions actions);

        Assert.True(accepted);
        Assert.Equal(confirm.Center, actions.Confirm);
        Assert.InRange(actions.Quantity.X, 880, 925);
        Assert.InRange(actions.Quantity.Y, 350, 380);
    }

    [Theory]
    [InlineData(515, 414, 71, 25, 600, 500, 62, 25)]
    [InlineData(515, 414, 71, 25, 1200, 414, 62, 25)]
    public void DialogActionsRejectUnsafeLayouts(
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
            new PixelRect(confirmX, confirmY, confirmWidth, confirmHeight),
            new PixelRect(cancelX, cancelY, cancelWidth, cancelHeight),
            new PixelSize(1366, 700),
            out _));
    }
}
