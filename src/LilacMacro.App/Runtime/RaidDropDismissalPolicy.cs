using LilacMacro.App.Debugging;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Runtime;

internal static class RaidDropDismissalPolicy
{
    // ExpeditionsMacro's field-proven resting action point, scaled from 808x611
    // to LilacMacro's canonical 1366x700 Roblox client.
    public static readonly PixelPoint ActionPoint = new(1324, 671);

    public static bool IsEnabled(WireGameMode gameMode, StoryAct act) =>
        gameMode == WireGameMode.Raid && act is StoryAct.Act2 or StoryAct.Act3;
}
