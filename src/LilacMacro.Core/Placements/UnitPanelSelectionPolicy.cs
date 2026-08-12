namespace LilacMacro.Core.Placements;

public static class UnitPanelSelectionPolicy
{
    public static bool AllowsPhantom(PlacementStepKind kind) => kind is
        PlacementStepKind.Place or
        PlacementStepKind.Reconfigure or
        PlacementStepKind.Sell;

    public static bool RequiresPhysical(PlacementStepKind kind) => kind == PlacementStepKind.Upgrade;
}
