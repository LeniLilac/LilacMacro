using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class MapPreparationPolicyTests
{
    [Theory]
    [MemberData(nameof(Plans))]
    public void ReturnsObservedFirstLoadMovement(
        string mapId,
        MapPreparationStep[] expected) =>
        Assert.Equal(expected, MapPreparationPolicy.For(mapId));

    [Fact]
    public void NoMovementMapHasEmptyPreparation() =>
        Assert.Empty(MapPreparationPolicy.For("event-villain-invasion-act-4"));

    public static TheoryData<string, MapPreparationStep[]> Plans => new()
    {
        {
            "event-villain-invasion-act-1",
            [new(0x57, 750), new(0x44, 750), new(0x57, 2200)]
        },
        {
            "event-villain-invasion-act-2",
            [new(0x57, 1000), new(0x41, 100), new(0x57, 1300)]
        },
        {
            "event-villain-invasion-act-3",
            [new(0x53, 900), new(0x44, 750)]
        },
        {
            MapPreparationPolicy.ExpeditionShop,
            [new(0x57, 2500), new(0x44, 750), new(0x57, 3300), new(0x44, 500)]
        },
    };
}
