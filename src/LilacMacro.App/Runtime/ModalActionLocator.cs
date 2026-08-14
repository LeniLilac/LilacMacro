using LilacMacro.Core.Datasets;

namespace LilacMacro.App.Runtime;

internal static class ModalActionLocator
{
    public static OcrTextRegion? FindStackedSelector(
        IReadOnlyList<OcrTextRegion> regions,
        Func<string, bool> isPrimary,
        Func<string, bool> isSecondary)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(isPrimary);
        ArgumentNullException.ThrowIfNull(isSecondary);

        OcrTextRegion[] primaries = regions.Where(region => isPrimary(region.Text)).ToArray();
        OcrTextRegion[] secondaries = regions.Where(region => isSecondary(region.Text)).ToArray();
        return primaries
            .SelectMany(primary => secondaries.Select(secondary => new
            {
                Primary = primary,
                Secondary = secondary,
                HorizontalDistance = Math.Abs(primary.Bounds.Center.X - secondary.Bounds.Center.X),
                VerticalDistance = secondary.Bounds.Center.Y - primary.Bounds.Center.Y,
            }))
            .Where(pair => pair.VerticalDistance > 0)
            .Where(pair => pair.HorizontalDistance <= Math.Max(
                pair.Primary.Bounds.Width,
                pair.Secondary.Bounds.Width) * 0.75)
            .Where(pair => pair.VerticalDistance <= Math.Max(
                pair.Primary.Bounds.Height,
                pair.Secondary.Bounds.Height) * 5)
            .OrderBy(pair => pair.HorizontalDistance)
            .ThenBy(pair => pair.VerticalDistance)
            .ThenByDescending(pair => pair.Primary.RecognitionConfidence)
            .Select(pair => pair.Primary)
            .FirstOrDefault();
    }

    public static OcrTextRegion? FindPairedAction(
        IReadOnlyList<OcrTextRegion> regions,
        Func<string, bool> isAction,
        Func<string, bool> isCancel)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(isAction);
        ArgumentNullException.ThrowIfNull(isCancel);

        OcrTextRegion[] actions = regions.Where(region => isAction(region.Text)).ToArray();
        OcrTextRegion[] cancels = regions.Where(region => isCancel(region.Text)).ToArray();
        return actions
            .SelectMany(action => cancels.Select(cancel => new
            {
                Action = action,
                Cancel = cancel,
                VerticalDistance = Math.Abs(action.Bounds.Center.Y - cancel.Bounds.Center.Y),
            }))
            .Where(pair => pair.Action.Bounds.Center.X < pair.Cancel.Bounds.Center.X)
            .Where(pair => pair.VerticalDistance <= Math.Max(
                pair.Action.Bounds.Height,
                pair.Cancel.Bounds.Height) * 1.5)
            .OrderBy(pair => pair.VerticalDistance)
            .ThenByDescending(pair => pair.Action.RecognitionConfidence)
            .Select(pair => pair.Action)
            .FirstOrDefault();
    }
}
