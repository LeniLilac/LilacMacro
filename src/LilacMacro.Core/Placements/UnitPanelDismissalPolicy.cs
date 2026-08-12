using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Placements;

public static class UnitPanelDismissalPolicy
{
    public const int SafeInsetPixels = 24;

    public static bool RequiresDismissal(PlacementStepKind kind) => kind is
        PlacementStepKind.Place or
        PlacementStepKind.Reconfigure or
        PlacementStepKind.Upgrade;

    public static PixelPoint ActionPoint(PixelSize clientSize)
    {
        if (clientSize.Width <= 0) throw new ArgumentOutOfRangeException(nameof(clientSize));
        if (clientSize.Height <= 0) throw new ArgumentOutOfRangeException(nameof(clientSize));
        return new PixelPoint(
            Math.Max(0, clientSize.Width - 1 - SafeInsetPixels),
            Math.Max(0, clientSize.Height - 1 - SafeInsetPixels));
    }
}
