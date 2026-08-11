using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public static class OcrRegionComposer
{
    public static IReadOnlyList<OcrTextRegion> AddAdjacentPairs(
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        List<OcrTextRegion> expanded = [.. regions];
        foreach (OcrTextRegion left in regions)
        {
            foreach (OcrTextRegion right in regions)
            {
                if (!CanCompose(left, right)) continue;
                PixelRect bounds = PixelRect.Union(left.Bounds, right.Bounds);
                string text = $"{left.Text} {right.Text}";
                if (expanded.Any(region =>
                        region.Bounds == bounds &&
                        string.Equals(region.Text, text, StringComparison.Ordinal)))
                {
                    continue;
                }

                expanded.Add(new OcrTextRegion
                {
                    Bounds = bounds,
                    Text = text,
                    DetectionConfidence = MinimumConfidence(
                        left.DetectionConfidence,
                        right.DetectionConfidence),
                    RecognitionConfidence = Math.Min(
                        left.RecognitionConfidence,
                        right.RecognitionConfidence),
                });
            }
        }
        return expanded;
    }

    private static bool CanCompose(OcrTextRegion left, OcrTextRegion right)
    {
        if (left == right ||
            OcrRuleEngine.Normalize(left.Text).Length == 0 ||
            OcrRuleEngine.Normalize(right.Text).Length == 0 ||
            right.Bounds.X < left.Bounds.X)
        {
            return false;
        }

        int overlap = Math.Min(left.Bounds.Bottom, right.Bounds.Bottom) -
            Math.Max(left.Bounds.Y, right.Bounds.Y);
        int minimumHeight = Math.Min(left.Bounds.Height, right.Bounds.Height);
        int gap = right.Bounds.X - left.Bounds.Right;
        int maximumGap = Math.Max(8, Math.Max(left.Bounds.Height, right.Bounds.Height));
        int maximumOutlineOverlap = Math.Max(3, minimumHeight / 2);
        double centerDifference = Math.Abs(
            left.Bounds.Center.Y - right.Bounds.Center.Y);
        return overlap >= Math.Ceiling(minimumHeight * 0.5) &&
            centerDifference <= Math.Max(3, minimumHeight * 0.5) &&
            gap >= -maximumOutlineOverlap &&
            gap <= maximumGap;
    }

    private static double? MinimumConfidence(double? first, double? second) =>
        first.HasValue && second.HasValue
            ? Math.Min(first.Value, second.Value)
            : first ?? second;
}
