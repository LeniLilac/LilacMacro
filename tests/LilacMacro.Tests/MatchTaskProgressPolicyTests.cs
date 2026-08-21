using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class MatchTaskProgressPolicyTests
{
    [Fact]
    public void ObservedAvailableTowerFloorStoresPriorClearedFloorAndClearsDefeats()
    {
        Dictionary<string, int> progress = [];
        Dictionary<string, int> defeats = new() { ["tower"] = 3 };

        MatchTaskProgressPolicy.ApplyObservedTowerAvailability(
            "tower", availableFloor: 6, progress, defeats);

        Assert.Equal(5, progress["tower"]);
        Assert.Empty(defeats);
    }

    [Fact]
    public void TowerVictoryStoresFloorAndClearsDefeats()
    {
        Dictionary<string, int> progress = new() { ["tower"] = 7 };
        Dictionary<string, int> defeats = new() { ["tower"] = 3 };

        MatchTaskProgressPolicy.Apply(
            "tower", "Trait Tower", isTower: true, victory: true,
            verifiedTowerFloor: 8, defeatLimit: 5, progress, defeats);

        Assert.Equal(8, progress["tower"]);
        Assert.False(defeats.ContainsKey("tower"));
    }

    [Fact]
    public void TowerDefeatStopsOnConfiguredCount()
    {
        Dictionary<string, int> progress = new() { ["tower"] = 7 };
        Dictionary<string, int> defeats = new() { ["tower"] = 4 };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            MatchTaskProgressPolicy.Apply(
                "tower", "Trait Tower", isTower: true, victory: false,
                verifiedTowerFloor: 8, defeatLimit: 5, progress, defeats));

        Assert.Equal(5, defeats["tower"]);
        Assert.Contains("5-defeat stop limit on floor 8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardTaskRetainsRetrySemantics()
    {
        Dictionary<string, int> victories = [];
        Dictionary<string, int> defeats = [];

        MatchTaskProgressPolicy.Apply(
            "story", "Story", isTower: false, victory: false,
            verifiedTowerFloor: 0, defeatLimit: 1, victories, defeats);

        Assert.Equal(1, defeats["story"]);
        Assert.Throws<InvalidOperationException>(() => MatchTaskProgressPolicy.Apply(
            "story", "Story", isTower: false, victory: false,
            verifiedTowerFloor: 0, defeatLimit: 1, victories, defeats));
    }
}
