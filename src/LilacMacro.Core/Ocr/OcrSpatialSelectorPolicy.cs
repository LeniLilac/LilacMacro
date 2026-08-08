using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Ocr;

public static class OcrSpatialSelectorPolicy
{
    public static OcrTextRegion? Select(
        OcrTextRegion configured,
        IReadOnlyCollection<OcrTextRegion> observed)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(observed);
        OcrTextRegion[] matches = observed
            .Where(candidate => OcrPhraseMatcher.Match(configured, candidate.Text).IsMatch)
            .ToArray();
        if (matches.Length == 0) return null;

        return configured.SpatialSelector switch
        {
            OcrSpatialSelector.Any => matches.Length == 1 ? matches[0] : null,
            OcrSpatialSelector.Leftmost => UniqueExtreme(matches, region => region.Bounds.Center.X, ascending: true),
            OcrSpatialSelector.Rightmost => UniqueExtreme(matches, region => region.Bounds.Center.X, ascending: false),
            OcrSpatialSelector.Topmost => UniqueExtreme(matches, region => region.Bounds.Center.Y, ascending: true),
            OcrSpatialSelector.Bottommost => UniqueExtreme(matches, region => region.Bounds.Center.Y, ascending: false),
            OcrSpatialSelector.SameRow => SelectSameRow(configured, matches, observed),
            OcrSpatialSelector.NearestAnchor => SelectNearest(configured, matches, observed),
            _ => null,
        };
    }

    private static OcrTextRegion? UniqueExtreme(
        IReadOnlyCollection<OcrTextRegion> matches,
        Func<OcrTextRegion, int> coordinate,
        bool ascending)
    {
        OcrTextRegion[] ordered = ascending
            ? matches.OrderBy(coordinate).ToArray()
            : matches.OrderByDescending(coordinate).ToArray();
        return ordered.Length == 1 || coordinate(ordered[0]) != coordinate(ordered[1]) ? ordered[0] : null;
    }

    private static OcrTextRegion? SelectSameRow(
        OcrTextRegion configured,
        IReadOnlyCollection<OcrTextRegion> matches,
        IReadOnlyCollection<OcrTextRegion> observed)
    {
        OcrTextRegion[] anchors = FindAnchors(configured, observed);
        OcrTextRegion[] aligned = matches.Where(candidate => anchors.Any(anchor =>
            candidate.Bounds.Y < anchor.Bounds.Bottom && candidate.Bounds.Bottom > anchor.Bounds.Y)).ToArray();
        return aligned.Length == 1 ? aligned[0] : null;
    }

    private static OcrTextRegion? SelectNearest(
        OcrTextRegion configured,
        IReadOnlyCollection<OcrTextRegion> matches,
        IReadOnlyCollection<OcrTextRegion> observed)
    {
        OcrTextRegion[] anchors = FindAnchors(configured, observed);
        if (anchors.Length == 0) return null;
        (OcrTextRegion Region, long Distance)[] ordered = matches
            .Select(candidate => (candidate, anchors.Min(anchor => DistanceSquared(candidate, anchor))))
            .OrderBy(item => item.Item2)
            .ToArray();
        return ordered.Length == 1 || ordered[0].Distance != ordered[1].Distance ? ordered[0].Region : null;
    }

    private static OcrTextRegion[] FindAnchors(
        OcrTextRegion configured,
        IEnumerable<OcrTextRegion> observed)
    {
        if (string.IsNullOrWhiteSpace(configured.SpatialAnchorText)) return [];
        return observed.Where(candidate => OcrPhraseMatcher.Match(
            configured.SpatialAnchorText,
            candidate.Text,
            OcrMatchMode.Exact).IsMatch).ToArray();
    }

    private static long DistanceSquared(OcrTextRegion first, OcrTextRegion second)
    {
        long x = first.Bounds.Center.X - second.Bounds.Center.X;
        long y = first.Bounds.Center.Y - second.Bounds.Center.Y;
        return x * x + y * y;
    }
}
