using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class UnitPanelDismissalPolicyTests
{
    [Theory]
    [InlineData(PlacementStepKind.Place)]
    [InlineData(PlacementStepKind.Reconfigure)]
    [InlineData(PlacementStepKind.Upgrade)]
    public void DismissesActionsThatLeaveSelectionOpen(PlacementStepKind kind) =>
        Assert.True(UnitPanelDismissalPolicy.RequiresDismissal(kind));

    [Theory]
    [InlineData(PlacementStepKind.Delay)]
    [InlineData(PlacementStepKind.Sell)]
    public void DoesNotAddClickAfterNonSelectionOrSelfClosingActions(PlacementStepKind kind) =>
        Assert.False(UnitPanelDismissalPolicy.RequiresDismissal(kind));

    [Fact]
    public void UsesExpeditionsSafeInsetOnCanonicalClient() =>
        Assert.Equal(
            new PixelPoint(1341, 675),
            UnitPanelDismissalPolicy.ActionPoint(new PixelSize(1366, 700)));

    [Fact]
    public void ClampsTinyPositiveClientToItsOnlyPoint() =>
        Assert.Equal(new PixelPoint(0, 0), UnitPanelDismissalPolicy.ActionPoint(new PixelSize(1, 1)));

    [Theory]
    [InlineData(0, 700)]
    [InlineData(1366, 0)]
    [InlineData(-1, 700)]
    public void RejectsInvalidClientSize(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnitPanelDismissalPolicy.ActionPoint(new PixelSize(width, height)));
}
