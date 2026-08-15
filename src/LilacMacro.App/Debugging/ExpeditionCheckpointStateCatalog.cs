using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class ExpeditionCheckpointStateCatalog
{
    public static readonly DebugStateSpec SpawnContinueSource = new(
        "EXPEDITION SPAWN CHECKPOINT CONTINUE",
        Dataset("expedition-spawn-checkpoint-20260812-214809"),
        [1],
        1,
        [new OcrTargetRule("Continue", "continue")],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Continue Button");

    public static readonly DebugStateSpec ContinueSource = new(
        "EXPEDITION CHECKPOINT CONTINUE",
        Dataset("expedition-post-start-checkpoint-20260812-215512"),
        [1],
        2,
        [
            new OcrTargetRule("Extract", "extract", "extr"),
            new OcrTargetRule("Continue", "continue"),
        ],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Button Area");

    public static readonly DebugStateSpec ContinueConfirmation = new(
        "EXPEDITION CHECKPOINT CONTINUE CONFIRMATION",
        Dataset("expedition-spawn-checkpoint-20260812-214809"),
        [1],
        3,
        [
            new OcrTargetRule("Continue Expedition", "continue expedition"),
            new OcrTargetRule("Continue", "continue"),
            new OcrTargetRule("Cancel", "cancel"),
        ],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Continue Confirm");

    public static readonly DebugStateSpec EncounterContinueSource = new(
        "EXPEDITION ENCOUNTER CONTINUE",
        Dataset("new-encounter-node-multi-ui-scales-20260814-082727"),
        [1, 3, 5],
        1,
        [new OcrTargetRule("Continue", "continue")],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Continue Button");

    public static readonly DebugStateSpec EncounterContinueConfirmation = new(
        "EXPEDITION ENCOUNTER CONTINUE CONFIRMATION",
        Dataset("new-encounter-node-multi-ui-scales-20260814-082727"),
        [2, 4, 6],
        3,
        [
            new OcrTargetRule("Continue Expedition", "continue expedition"),
            new OcrTargetRule("Continue", "continue"),
            new OcrTargetRule("Cancel", "cancel"),
        ],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Continue Confirm");

    public static readonly DebugStateSpec ExtractSource = new(
        "EXPEDITION CHECKPOINT EXTRACT",
        Dataset("expedition-post-start-checkpoint-20260812-215512"),
        [1],
        2,
        [
            new OcrTargetRule("Extract", "extract", "extr"),
            new OcrTargetRule("Continue", "continue"),
        ],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Button Area");

    public static readonly DebugStateSpec ExtractConfirmation = new(
        "EXPEDITION CHECKPOINT EXTRACT CONFIRMATION",
        Dataset("expedition-post-start-checkpoint-20260812-215512"),
        [2],
        3,
        [
            new OcrTargetRule("Extraction", "extraction"),
            new OcrTargetRule("Extract", "extract"),
            new OcrTargetRule("Cancel", "cancel"),
        ],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Confirm Area");

    public static IEnumerable<DebugStateSpec> All()
    {
        yield return SpawnContinueSource;
        yield return ContinueSource;
        yield return ContinueConfirmation;
        yield return EncounterContinueSource;
        yield return EncounterContinueConfirmation;
        yield return ExtractSource;
        yield return ExtractConfirmation;
    }

    private static string Dataset(string directory) => RuntimeEvidenceDatasetCatalog.Dataset(directory);
}
