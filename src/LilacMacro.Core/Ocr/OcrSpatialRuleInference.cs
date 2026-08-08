using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public static class OcrSpatialRuleInference
{
    private const double MinimumSeparation = 0.03;

    public static OcrSpatialSelector Infer(
        OcrTextRegion target,
        IReadOnlyCollection<OcrTextRegion> candidates,
        PixelRect coarseRegion)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(candidates);
        string normalized = OcrPhraseMatcher.Normalize(target.Text);
        OcrTextRegion[] matches = candidates
            .Where(candidate => OcrPhraseMatcher.Normalize(candidate.Text) == normalized)
            .ToArray();
        if (normalized.Length == 0 || matches.Length < 2) return OcrSpatialSelector.Any;

        int x = target.Bounds.Center.X;
        int y = target.Bounds.Center.Y;
        List<(OcrSpatialSelector Selector, double Separation)> options = [];
        AddIfExtreme(options, OcrSpatialSelector.Leftmost, x == matches.Min(item => item.Bounds.Center.X),
            matches.Where(item => !ReferenceEquals(item, target)).Min(item => Math.Abs(item.Bounds.Center.X - x)) /
            (double)Math.Max(1, coarseRegion.Width));
        AddIfExtreme(options, OcrSpatialSelector.Topmost, y == matches.Min(item => item.Bounds.Center.Y),
            matches.Where(item => !ReferenceEquals(item, target)).Min(item => Math.Abs(item.Bounds.Center.Y - y)) /
            (double)Math.Max(1, coarseRegion.Height));
        AddIfExtreme(options, OcrSpatialSelector.Rightmost, x == matches.Max(item => item.Bounds.Center.X),
            matches.Where(item => !ReferenceEquals(item, target)).Min(item => Math.Abs(item.Bounds.Center.X - x)) /
            (double)Math.Max(1, coarseRegion.Width));
        AddIfExtreme(options, OcrSpatialSelector.Bottommost, y == matches.Max(item => item.Bounds.Center.Y),
            matches.Where(item => !ReferenceEquals(item, target)).Min(item => Math.Abs(item.Bounds.Center.Y - y)) /
            (double)Math.Max(1, coarseRegion.Height));

        return options
            .Where(option => option.Separation >= MinimumSeparation)
            .OrderByDescending(option => option.Separation)
            .ThenBy(option => option.Selector)
            .Select(option => option.Selector)
            .FirstOrDefault();
    }

    private static void AddIfExtreme(
        ICollection<(OcrSpatialSelector Selector, double Separation)> options,
        OcrSpatialSelector selector,
        bool condition,
        double separation)
    {
        if (condition) options.Add((selector, separation));
    }
}
