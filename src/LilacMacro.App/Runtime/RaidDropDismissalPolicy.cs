using LilacMacro.App.Debugging;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Runtime;

internal static class RaidDropDismissalPolicy
{
    public static readonly PixelPoint ActionPoint =
        UnitPanelDismissalPolicy.ActionPoint(DebugWorkflowCatalog.ClientSize);

    public static bool IsEnabled(WireGameMode gameMode, StoryAct act) =>
        gameMode == WireGameMode.Raid && act is StoryAct.Act2 or StoryAct.Act3;
}
