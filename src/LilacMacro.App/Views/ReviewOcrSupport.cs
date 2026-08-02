using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Views;

internal static class ReviewOcrSupport
{
    public static OcrTrial[] Latest(BoxAnnotation? annotation) => annotation?.OcrTrials
        .GroupBy(trial => (trial.ModelName, trial.Device))
        .Select(group => group.OrderByDescending(trial => trial.RanAtUtc).First())
        .OrderBy(trial => trial.ModelName, StringComparer.Ordinal)
        .ThenBy(trial => trial.Device, StringComparer.Ordinal)
        .ToArray() ?? [];

    public static OcrResultItem[] Present(IEnumerable<OcrTrial> trials) => trials.Select(trial => new OcrResultItem(
        trial.ModelName,
        string.IsNullOrWhiteSpace(trial.Text) ? "No text" : trial.Text,
        $"{trial.Regions.Count} BOX  ·  confidence {trial.Confidence:P2}",
        Timings(trial),
        LineData(trial),
        $"{trial.Device.ToUpperInvariant()}  ·  {trial.DetectorModelName}  ·  PaddleOCR {trial.RuntimeVersion}  ·  {trial.RanAtUtc.LocalDateTime:g}"))
        .ToArray();

    public static OcrTrial CreateTrial(OcrWorkerResult result) => new()
    {
        ModelName = result.ModelName,
        DetectorModelName = result.DetectorModelName,
        Device = result.Device,
        Text = result.Text,
        Confidence = result.Confidence,
        ModelLoadMilliseconds = result.ModelLoadMilliseconds,
        InferenceMilliseconds = result.InferenceMilliseconds,
        RuntimeVersion = result.PaddleOcrVersion,
        RanAtUtc = DateTimeOffset.UtcNow,
        ModelWasCached = result.ModelCached,
        Regions = result.Regions.Select(region => new OcrTextRegion
        {
            Bounds = new PixelRect(region.X, region.Y, region.Width, region.Height),
            Text = region.Text,
            DetectionConfidence = region.DetectionConfidence,
            RecognitionConfidence = region.RecognitionConfidence,
        }).ToList(),
    };

    public static OcrTrial? Latest(BoxAnnotation? annotation, string model, string device) => annotation?.OcrTrials
        .Where(item => item.ModelName == model && item.Device == device)
        .OrderByDescending(item => item.RanAtUtc)
        .FirstOrDefault()
        ?? annotation?.OcrTrials.OrderByDescending(item => item.RanAtUtc).FirstOrDefault();

    private static string Timings(OcrTrial trial)
    {
        string load = trial.ModelWasCached ? "load cached" : $"load {trial.ModelLoadMilliseconds} ms";
        return $"{load}  ·  inference {trial.InferenceMilliseconds} ms  ·  total {trial.ModelLoadMilliseconds + trial.InferenceMilliseconds} ms";
    }

    private static string LineData(OcrTrial trial) => string.Join(
        Environment.NewLine,
        trial.Regions.Select((region, index) =>
            $"{index + 1:00} [{region.Bounds.X},{region.Bounds.Y},{region.Bounds.Width},{region.Bounds.Height}]  " +
            $"det {FormatConfidence(region.DetectionConfidence)}  rec {region.RecognitionConfidence:P1}  {region.Text}"));

    private static string FormatConfidence(double? confidence) => confidence is { } value ? value.ToString("P1") : "-";
}
