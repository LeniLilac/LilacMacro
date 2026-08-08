namespace LilacMacro.Core.Placements;

public sealed record PlacementStep
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public PlacementStepKind Kind { get; init; }

    public Guid? TargetPlacementId { get; init; }

    public int UnitSlot { get; init; } = 1;

    public int X { get; init; }

    public int Y { get; init; }

    public int DelayAfterMilliseconds { get; init; } = PlacementSetupRules.DefaultStepDelayMilliseconds;

    public PlacementTargetingPriority TargetingPriority { get; init; } = PlacementTargetingPriority.First;

    public PlacementAutoUpgradePriority AutoUpgradePriority { get; init; } = PlacementAutoUpgradePriority.Priority1;

    public bool ChangeTargetingPriority { get; init; }

    public PlacementAutoUpgradeAction AutoUpgradeAction { get; init; }

    public int DelayDurationMilliseconds { get; init; }

    public int UpgradeCount { get; init; }

    public static PlacementStep CreateStartGame() => new()
    {
        Kind = PlacementStepKind.StartGame,
        DelayAfterMilliseconds = 0,
    };

    public static PlacementStep CreatePlace(
        int unitSlot,
        int x,
        int y,
        int delayAfterMilliseconds,
        PlacementTargetingPriority targetingPriority,
        PlacementAutoUpgradePriority autoUpgradePriority) => new()
        {
            Kind = PlacementStepKind.Place,
            UnitSlot = unitSlot,
            X = x,
            Y = y,
            DelayAfterMilliseconds = delayAfterMilliseconds,
            TargetingPriority = targetingPriority,
            AutoUpgradePriority = autoUpgradePriority,
        };
}
