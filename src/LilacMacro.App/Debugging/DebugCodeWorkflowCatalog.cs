using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class DebugCodeWorkflowCatalog
{
    public static readonly DebugStateSpec Launcher = new(
        "Codes Launcher",
        RuntimeEvidenceDatasetCatalog.Dataset("possible-offet-code-redeem-path-20260814-011731"),
        [1],
        3,
        [
            new OcrTargetRule("Join Friend", "join friend"),
            new OcrTargetRule("Redeem Codes", "redeem codes"),
            new OcrTargetRule("Lobby Music", "lobby music"),
        ],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Codes Launcher State");

    public static readonly DebugStateSpec Panel = new(
        "Codes Panel",
        RuntimeEvidenceDatasetCatalog.Dataset("code-redeem-path-20260814-010435"),
        [1, 2],
        2,
        [
            new OcrTargetRule("Codes", "codes"),
            new OcrTargetRule("Redeem Code", "redeem code"),
        ],
        DebugMatchMode.ExactTargets,
        RegionLabel: "Codes Panel State");

    public static readonly OcrTargetRule Input = new("Enter Code", "enter code");
    public static readonly OcrTargetRule Redeem = new("Redeem Code", "redeem code");

    public static IEnumerable<DebugStateSpec> All()
    {
        yield return Launcher;
        yield return Panel;
    }
}
