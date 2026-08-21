using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class TowerRunPolicyTests
{
    [Theory]
    [InlineData("Floor 1", 1)]
    [InlineData("Traitless Tower Floor 25 - Rose Kingdom", 25)]
    [InlineData("FLOOR100", 100)]
    public void ParsesObservedFloorText(string text, int expected)
    {
        Assert.True(TowerRunPolicy.TryParseFloor(text, out int floor));
        Assert.Equal(expected, floor);
    }

    [Fact]
    public void SelectsTopmostThenRightmostFloorText()
    {
        OcrTextRegion lower = Region("Floor 99", 700, 300);
        OcrTextRegion topLeft = Region("Floor 2", 500, 100);
        OcrTextRegion topRight = Region("Floor 3", 800, 100);

        TowerFloorSelection selection = Assert.IsType<TowerFloorSelection>(
            TowerRunPolicy.SelectTopRightFloor([lower, topLeft, topRight]));

        Assert.Equal(3, selection.Floor);
        Assert.Same(topRight, selection.Region);
    }

    [Fact]
    public void SelectsStandaloneFloorLabelWhenNumberIsRenderedSeparately()
    {
        OcrTextRegion floor = Region("Floor", 760, 100);
        OcrTextRegion number = Region("12", 790, 130);

        TowerFloorSelection selection = Assert.IsType<TowerFloorSelection>(
            TowerRunPolicy.SelectTopRightFloor([floor, number]));

        Assert.Equal(0, selection.Floor);
        Assert.Same(floor, selection.Region);
    }

    [Theory]
    [InlineData(4, 5, false)]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, true)]
    public void StopsOnConfiguredDefeatCount(int defeats, int limit, bool expected) =>
        Assert.Equal(expected, TowerRunPolicy.ShouldStopAfterDefeat(defeats, limit));

    [Fact]
    public void VictoryAdvancesToVerifiedFloorAndResetsFloorDefeats()
    {
        TowerTerminalState state = TowerRunPolicy.ApplyTerminalOutcome(
            victory: true, currentProgress: 8, defeatsOnFloor: 3, verifiedFloor: 9, defeatsBeforeStop: 5);

        Assert.Equal(new TowerTerminalState(9, 0, false, false), state);
    }

    [Theory]
    [InlineData(3, 4, false, true)]
    [InlineData(4, 5, true, false)]
    public void DefeatCountsOnCurrentFloorAndStopsAtLimit(
        int priorDefeats,
        int expectedDefeats,
        bool shouldStop,
        bool shouldRepeat)
    {
        TowerTerminalState state = TowerRunPolicy.ApplyTerminalOutcome(
            victory: false, currentProgress: 8, priorDefeats, verifiedFloor: 9, defeatsBeforeStop: 5);

        Assert.Equal(new TowerTerminalState(8, expectedDefeats, shouldStop, shouldRepeat), state);
    }

    [Theory]
    [InlineData(TowerRunPolicy.TraitRoute, TowerType.Trait, TowerRunPolicy.TraitPlacementRouteId)]
    [InlineData(TowerRunPolicy.TraitlessRoute, TowerType.Traitless, TowerRunPolicy.TraitlessPlacementRouteId)]
    public void TowerTypeOwnsPlanAndPlacementRoutes(string route, TowerType type, string placementRoute)
    {
        Assert.Equal(type, TowerRunPolicy.ParseType(route));
        Assert.Equal(placementRoute, TowerRunPolicy.PlacementRouteId(type));
    }

    [Theory]
    [InlineData(TowerType.Trait, "Tower")]
    [InlineData(TowerType.Traitless, "Traitless Tower")]
    public void TowerTypeUsesObservedGameSelectionLabel(TowerType type, string expected) =>
        Assert.Equal(expected, TowerRunPolicy.SelectionLabel(type));

    [Fact]
    public void TowerModeRevealUsesClientCenterAndBoundedDownwardBursts()
    {
        Assert.Equal(
            new PixelPoint(683, 350),
            TowerRunPolicy.ModeRevealScrollAnchor(new PixelSize(1366, 700)));
        Assert.Equal(-5000, TowerRunPolicy.ModeRevealScrollWheelDelta);
        Assert.Equal(3, TowerRunPolicy.MaximumModeRevealScrollAttempts);
        Assert.Equal(1, TowerRunPolicy.MaximumModeTransitionActionAttempts);
    }

    [Theory]
    [InlineData(0, 700)]
    [InlineData(1366, 0)]
    public void TowerModeRevealRejectsInvalidClientSize(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TowerRunPolicy.ModeRevealScrollAnchor(new PixelSize(width, height)));

    private static OcrTextRegion Region(string text, int x, int y) => new()
    {
        Bounds = new PixelRect(x, y, 80, 20),
        Text = text,
        RecognitionConfidence = 0.99,
    };
}
