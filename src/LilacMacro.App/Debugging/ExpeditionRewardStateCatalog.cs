using LilacMacro.Core.Automation;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class ExpeditionRewardStateCatalog
{
    public static readonly DebugStateSpec Popup = new(
        "EXPEDITION LEVEL REWARD POPUP",
        Dataset("expedition-reward-collect-20260818-205321"),
        [1],
        ExpeditionRewardPopupPolicy.MinimumSelectUpgradeMatches,
        [ExpeditionRewardPopupPolicy.SelectUpgradeTarget],
        DebugMatchMode.RepeatedTarget,
        RegionLabel: "Expedition Reward Popup Action Strip");

    private static string Dataset(string directory) =>
        RuntimeEvidenceDatasetCatalog.Dataset(directory);

    public static IEnumerable<DebugStateSpec> All()
    {
        yield return Popup;
    }
}
