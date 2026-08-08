using LilacMacro.App.Diagnostics;

namespace LilacMacro.App.Debugging;

internal static class WireDebugEvidence
{
    public static void RecordComparisons(
        DeepDebugSessionService deepDebug,
        IReadOnlyList<WireVisualComparison> comparisons)
    {
        foreach (WireVisualComparison comparison in comparisons)
        {
            var context = new { comparison.State, comparison.Label, comparison.ImageBounds };
            deepDebug.RecordGrayImage(comparison.MedianPreview, "visual-profile", context);
            deepDebug.RecordGrayImage(comparison.ReliabilityPreview, "visual-reliability", context);
            deepDebug.RecordGrayImage(comparison.MatchedPreview, "visual-live-crop", context);
        }
    }

    public static object Snapshot(DebugOcrSnapshot snapshot) => new
    {
        snapshot.State,
        snapshot.Source,
        snapshot.RegionOfInterest,
        Ocr = new
        {
            snapshot.Ocr.Device,
            snapshot.Ocr.ModelName,
            snapshot.Ocr.DetectorModelName,
            snapshot.Ocr.ModelLoadMilliseconds,
            snapshot.Ocr.InferenceMilliseconds,
            TotalMilliseconds = snapshot.Ocr.ModelLoadMilliseconds + snapshot.Ocr.InferenceMilliseconds,
            snapshot.Ocr.ModelCached,
            snapshot.Ocr.PaddleOcrVersion,
        },
        Regions = snapshot.Regions.Select(region => new
        {
            region.Text,
            region.Bounds,
            region.DetectionConfidence,
            region.RecognitionConfidence,
        }).ToArray(),
        Evaluation = snapshot.Evaluation,
        snapshot.VisualAnchors,
    };
}
