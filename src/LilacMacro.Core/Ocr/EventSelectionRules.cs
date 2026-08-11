using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Ocr;

public enum EventDestination
{
    VillainInvasion,
    BossBounty,
    GuessThatUnit,
}

public static class EventSelectionRules
{
    private static readonly OcrTargetRule VillainInvasion = new(
        "Villain Invasion",
        "villain invasion",
        "invasion");

    private static readonly OcrTargetRule BossBounty = new(
        "Boss Bounty",
        "boss bounty");

    private static readonly OcrTargetRule GuessThatUnit = new(
        "Guess That Unit",
        "guess that unit");

    public static IReadOnlyList<OcrTargetRule> StateTargets { get; } =
    [
        new("Events", "events"),
        new("Back", "back"),
        new("Calendar", "calendar"),
    ];

    public static OcrTargetRule TargetFor(EventDestination destination) => destination switch
    {
        EventDestination.VillainInvasion => VillainInvasion,
        EventDestination.BossBounty => BossBounty,
        EventDestination.GuessThatUnit => GuessThatUnit,
        _ => throw new ArgumentOutOfRangeException(nameof(destination)),
    };

    public static OcrTargetMatch? Find(
        EventDestination destination,
        IReadOnlyList<OcrTextRegion> regions) =>
        OcrRuleEngine.FindTarget(TargetFor(destination), regions);
}
