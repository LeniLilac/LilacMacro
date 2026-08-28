using LilacMacro.Core.Imaging;

namespace LilacMacro.Windows.Capture;

internal static class CaptureFrameAnalyzer
{
    public static CaptureFrameDiagnostics Analyze(RgbImage image)
    {
        long luminanceTotal = 0;
        int nearWhite = 0;
        int clipped = 0;
        int dark = 0;
        int[] histogram = new int[256];
        ReadOnlySpan<byte> pixels = image.Pixels;
        int pixelCount = pixels.Length / 3;
        for (int offset = 0; offset < pixels.Length; offset += 3)
        {
            byte red = pixels[offset];
            byte green = pixels[offset + 1];
            byte blue = pixels[offset + 2];
            int luminance = (54 * red + 183 * green + 19 * blue + 128) >> 8;
            luminanceTotal += luminance;
            histogram[luminance]++;
            if (luminance >= 242) nearWhite++;
            if (red >= 254 || green >= 254 || blue >= 254) clipped++;
            if (luminance <= 13) dark++;
        }

        int p95Target = Math.Max(1, (int)Math.Ceiling(pixelCount * 0.95));
        int cumulative = 0;
        int p95 = 255;
        for (int luminance = 0; luminance < histogram.Length; luminance++)
        {
            cumulative += histogram[luminance];
            if (cumulative < p95Target) continue;
            p95 = luminance;
            break;
        }

        return new CaptureFrameDiagnostics(
            luminanceTotal / (double)pixelCount / 255d,
            p95 / 255d,
            nearWhite * 100d / pixelCount,
            clipped * 100d / pixelCount,
            dark * 100d / pixelCount);
    }
}
