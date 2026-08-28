using LilacMacro.Core.Imaging;

namespace LilacMacro.Windows.Capture;

internal static class CaptureExposurePolicy
{
    public const int ObservationWindowSize = 24;
    public const int MaximumProbeAttempts = 2;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    public static bool IsEligibleObservation(CaptureFrameDiagnostics frame) =>
        frame.MeanSrgbLuminance is >= 0.10 and <= 0.80 &&
        frame.NearWhitePixelPercent <= 15.0 &&
        frame.DarkPixelPercent <= 80.0;

    public static bool IsSuspiciousWindow(IReadOnlyCollection<CaptureFrameDiagnostics> observations)
    {
        if (observations.Count < ObservationWindowSize) return false;
        double[] clipped = observations.Select(frame => frame.ClippedPixelPercent).Order().ToArray();
        double[] p95 = observations.Select(frame => frame.P95SrgbLuminance).Order().ToArray();
        int median = observations.Count / 2;
        return clipped[median] >= 8.0 && p95[median] >= 0.90;
    }

    public static bool ShouldAcceptFallback(
        CaptureFrameDiagnostics source,
        CaptureFrameDiagnostics candidate,
        double structuralCorrelation) =>
        structuralCorrelation >= 0.65 &&
        candidate.MeanSrgbLuminance >= 0.08 &&
        candidate.MeanSrgbLuminance <= source.MeanSrgbLuminance * 0.90 &&
        candidate.P95SrgbLuminance < source.P95SrgbLuminance &&
        candidate.ClippedPixelPercent <= source.ClippedPixelPercent * 0.50 &&
        candidate.ClippedPixelPercent <= source.ClippedPixelPercent - 3.0 &&
        candidate.DarkPixelPercent <= 45.0;

    public static double MeasureStructuralCorrelation(RgbImage first, RgbImage second)
    {
        if (first.Size != second.Size) return 0;

        ReadOnlySpan<byte> a = first.Pixels;
        ReadOnlySpan<byte> b = second.Pixels;
        int pixelCount = a.Length / 3;
        int step = Math.Max(1, pixelCount / 8192);
        double sumA = 0;
        double sumB = 0;
        double sumAA = 0;
        double sumBB = 0;
        double sumAB = 0;
        int count = 0;
        for (int pixel = 0; pixel < pixelCount; pixel += step)
        {
            int offset = pixel * 3;
            double valueA = Luminance(a, offset);
            double valueB = Luminance(b, offset);
            sumA += valueA;
            sumB += valueB;
            sumAA += valueA * valueA;
            sumBB += valueB * valueB;
            sumAB += valueA * valueB;
            count++;
        }

        if (count < 2) return 0;
        double covariance = count * sumAB - sumA * sumB;
        double varianceA = count * sumAA - sumA * sumA;
        double varianceB = count * sumBB - sumB * sumB;
        double denominator = Math.Sqrt(Math.Max(0, varianceA) * Math.Max(0, varianceB));
        return denominator <= 0 ? 0 : Math.Clamp(covariance / denominator, -1, 1);
    }

    private static double Luminance(ReadOnlySpan<byte> pixels, int offset) =>
        (54 * pixels[offset] + 183 * pixels[offset + 1] + 19 * pixels[offset + 2]) / 256d;
}
