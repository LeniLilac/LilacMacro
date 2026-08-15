namespace LilacMacro.Core.Placements;

public sealed class PlacementRouteSetup
{
    public required string RouteId { get; set; }

    public int TeamSlot { get; set; } = 1;

    public int SelectedUnitSlot { get; set; } = 1;

    public int BetweenUpgradeAttemptsMilliseconds { get; set; } =
        PlacementSetupRules.DefaultBetweenUpgradeAttemptsMilliseconds;

    public PlacementTargetingPriority DefaultTargetingPriority { get; set; } = PlacementTargetingPriority.First;

    public PlacementAutoUpgradePriority DefaultAutoUpgradePriority { get; set; } =
        PlacementAutoUpgradePriority.Priority1;

    public List<PlacementStep> Steps { get; set; } = [PlacementStep.CreateStartGame()];
}

public sealed class PlacementSetupDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public required string MapId { get; set; }

    public required int ImageWidth { get; set; }

    public required int ImageHeight { get; set; }

    public required PlacementRouteSetup Shared { get; set; }

    public Dictionary<string, PlacementRouteSetup> Overrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
