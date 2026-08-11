using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public sealed record TeamLoadConfirmLayout(
    PixelRect ConfirmBounds,
    PixelRect CancelBounds)
{
    public PixelPoint ConfirmPoint => ConfirmBounds.Center;
    public PixelPoint CancelPoint => CancelBounds.Center;

    public static TeamLoadConfirmLayout? TryCreate(IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        OcrTextRegion? confirm = FindExact(regions, "confirm");
        OcrTextRegion? cancel = FindContaining(regions, "cancel");
        return confirm is null || cancel is null
            ? null
            : new TeamLoadConfirmLayout(confirm.Bounds, cancel.Bounds);
    }

    private static OcrTextRegion? FindExact(
        IReadOnlyList<OcrTextRegion> regions,
        string target) => regions
        .Where(region => OcrRuleEngine.Normalize(region.Text)
            .Equals(target, StringComparison.Ordinal))
        .OrderByDescending(region => region.Bounds.Y)
        .ThenBy(region => region.Bounds.X)
        .FirstOrDefault();

    internal static OcrTextRegion? FindContaining(
        IReadOnlyList<OcrTextRegion> regions,
        string target) => regions
        .Where(region => OcrRuleEngine.Normalize(region.Text)
            .Contains(target, StringComparison.Ordinal))
        .OrderByDescending(region => region.Bounds.Y)
        .ThenBy(region => region.Bounds.X)
        .FirstOrDefault();
}

public sealed record TeamIncludeEquipmentLayout(
    PixelRect IncludeBounds,
    PixelRect ExcludeBounds,
    PixelRect CancelBounds)
{
    public PixelPoint IncludePoint => IncludeBounds.Center;

    public static TeamIncludeEquipmentLayout? TryCreate(
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        OcrTextRegion? include = regions
            .Where(region => OcrRuleEngine.Normalize(region.Text)
                .Equals("include", StringComparison.Ordinal))
            .OrderByDescending(region => region.Bounds.Y)
            .ThenBy(region => region.Bounds.X)
            .FirstOrDefault();
        OcrTextRegion? exclude = TeamLoadConfirmLayout.FindContaining(regions, "exclude");
        OcrTextRegion? cancel = TeamLoadConfirmLayout.FindContaining(regions, "cancel");
        return include is null || exclude is null || cancel is null
            ? null
            : new TeamIncludeEquipmentLayout(
                include.Bounds,
                exclude.Bounds,
                cancel.Bounds);
    }
}
