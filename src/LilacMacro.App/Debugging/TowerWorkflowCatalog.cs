using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class TowerWorkflowCatalog
{
    private static readonly IReadOnlyList<OcrTargetRule> StageTargets =
    [
        new("Select Stage", "select stage"),
    ];

    private static readonly IReadOnlyList<OcrTargetRule> PreviewMapFloorTargets =
    [
        new("Floor", "floor"),
        .. DebugWorkflowCatalog.MapTargets,
    ];

    public static readonly DebugStateSpec TowerSelect = new(
        "TOWER SELECT",
        Dataset("tower-select-ui-20260810-173818"),
        [1, 2, 3],
        4,
        DebugWorkflowTargets.TowerSelect,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Rewards", "Leaderboard", "Back"],
        PoolTargetNames: ["Tower", "Traitless Tower"],
        MinimumPoolMatches: 1,
        RegionLabel: "Tower Select State");

    public static readonly DebugStateSpec TowerFloorList = new(
        "TOWER FLOOR LIST",
        Dataset("tower-multi-floor-ui-20260810-175955"),
        [1, 2, 3],
        1,
        DebugWorkflowTargets.TowerFloors,
        DebugMatchMode.RepeatedTarget,
        RegionLabel: "Tower Floor List");

    public static readonly DebugStateSpec TowerStage = new(
        "TOWER STAGE",
        Dataset("tower-map-select-stage-preview-20260810-174515"),
        [1, 5, 6],
        1,
        StageTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Select Stage"],
        RegionLabel: "Tower Stage State");

    public static readonly DebugStateSpec TowerPreviewMapFloor = new(
        "TOWER PREVIEW MAP + FLOOR",
        Dataset("tower-map-select-stage-preview-20260810-174515"),
        [2, 3, 4],
        2,
        PreviewMapFloorTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Floor"],
        PoolTargetNames: DebugWorkflowCatalog.MapTargets.Select(target => target.Name).ToArray(),
        MinimumPoolMatches: 1,
        RegionLabel: "Map+Floor Detect ROI");

    public static IEnumerable<DebugStateSpec> All()
    {
        yield return TowerSelect;
        yield return TowerFloorList;
        yield return TowerStage;
        yield return TowerPreviewMapFloor;
    }

    private static string Dataset(string directory) => RuntimeEvidenceDatasetCatalog.Dataset(directory);
}
