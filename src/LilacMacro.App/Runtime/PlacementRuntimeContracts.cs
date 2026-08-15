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

internal sealed class ExpeditionPlacementSession(
    IReadOnlyList<PlacementExecutionState> activePlacements,
    UnitPanelLayout? panelLayout)
{
    private readonly HashSet<Guid> _retainedPhysicalPlacements = [];

    public IReadOnlyList<PlacementExecutionState> ActivePlacements { get; } = activePlacements;

    public UnitPanelLayout? PanelLayout { get; } = panelLayout;

    public IReadOnlyList<PlacementExecutionState> ReplayCandidates => ActivePlacements
        .Where(state => !_retainedPhysicalPlacements.Contains(state.Placement.Id))
        .ToArray();

    public bool IsRetainedPhysical(Guid placementId) =>
        _retainedPhysicalPlacements.Contains(placementId);

    public void MarkRetainedPhysical(Guid placementId)
    {
        if (ActivePlacements.All(state => state.Placement.Id != placementId))
            throw new ArgumentOutOfRangeException(nameof(placementId));
        _retainedPhysicalPlacements.Add(placementId);
    }
}
