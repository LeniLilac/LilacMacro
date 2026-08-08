using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Runtime;

internal sealed record PlacementRuntimeKeys(
    int QuickPlacement = 'Q',
    int CancelPlacement = 'Z',
    int ChangeTargeting = 'T',
    int ChangeAutoUpgrade = 'K',
    int Upgrade = 'E',
    int Sell = 'X',
    int ReservedVirtualKey = 0x75);

internal enum MatchTerminalOutcome
{
    Victory,
    Defeat,
}

internal sealed record PlacementRuntimeResult(
    MatchTerminalOutcome Outcome,
    bool Repeated,
    int ExecutedSteps);

internal sealed class PlacementExecutionState(
    PlacementStep placement,
    PixelPoint livePoint)
{
    public PlacementStep Placement { get; } = placement;

    public PixelPoint LivePoint { get; } = livePoint;

    public PlacementTargetingPriority Targeting { get; set; } = PlacementTargetingPriority.First;

    public PlacementAutoUpgradePriority AutoUpgrade { get; set; } = PlacementAutoUpgradePriority.Off;
}
