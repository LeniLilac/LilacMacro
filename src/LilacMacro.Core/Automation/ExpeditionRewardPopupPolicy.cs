using LilacMacro.Core.Datasets;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Automation;

public static class ExpeditionRewardPopupPolicy
{
    public const int MinimumSelectUpgradeMatches = 2;
    public const int MaximumConsecutivePopups = 8;
    public const int MaximumObservationAttempts = 30;

    public static readonly OcrTargetRule SelectUpgradeTarget = new(
        "Select Upgrade",
        "select upgrade",
        "selectupgrade");

    public static IReadOnlyList<OcrTextRegion> FindSelectUpgradeButtons(
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return regions
            .Where(region => OcrPhraseMatcher.Match(
                "Select Upgrade", region.Text, OcrMatchMode.FuzzyPhrase).IsMatch)
            .OrderBy(region => region.Bounds.Center.X)
            .ThenBy(region => region.Bounds.Center.Y)
            .ToArray();
    }

    public static bool IsPopup(IReadOnlyList<OcrTextRegion> regions)
    {
        IReadOnlyList<OcrTextRegion> buttons = FindSelectUpgradeButtons(regions);
        return buttons.Count >= MinimumSelectUpgradeMatches && SameActionRow(buttons);
    }

    public static bool HasBlockingEvidence(IReadOnlyList<OcrTextRegion> regions) =>
        FindSelectUpgradeButtons(regions).Count > 0;

    public static OcrTextRegion? SelectRightmost(
        IReadOnlyList<OcrTextRegion> regions)
    {
        if (!IsPopup(regions)) return null;

        IReadOnlyList<OcrTextRegion> buttons = FindSelectUpgradeButtons(regions);
        int rightmost = buttons.Max(region => region.Bounds.Center.X);
        OcrTextRegion[] matches = buttons
            .Where(region => region.Bounds.Center.X == rightmost)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool SameActionRow(IReadOnlyList<OcrTextRegion> buttons)
    {
        int minimum = buttons.Min(region => region.Bounds.Center.Y);
        int maximum = buttons.Max(region => region.Bounds.Center.Y);
        int tolerance = Math.Max(12, buttons.Max(region => region.Bounds.Height) * 2);
        return maximum - minimum <= tolerance;
    }
}
