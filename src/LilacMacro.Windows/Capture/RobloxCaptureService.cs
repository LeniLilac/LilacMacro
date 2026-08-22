using LilacMacro.Core.Imaging;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Vision;

namespace LilacMacro.Windows.Capture;

public sealed class RobloxCaptureService(RobloxWindowService windows) : IDisposable
{
    private readonly WindowsGraphicsCapture _capture = new();

    public CapturedPng Capture(RobloxWindow window)
    {
        nint handle = windows.Revalidate(window);
        ClientBounds client = windows.GetClientBounds(window);
        WindowBounds bounds = windows.GetWindowBounds(window);
        WindowBounds extended = windows.GetExtendedFrameBounds(window) ?? bounds;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                RgbImage image = _capture.CaptureClient(
                    handle,
                    window.ProcessId,
                    client,
                    bounds,
                    extended);
                return new CapturedPng(
                    image.Size,
                    PngEncoder.Encode(image),
                    _capture.LastColorDiagnostics,
                    AnalyzeFrame(image));
            }
            catch (CaptureSurfaceChangedException) when (attempt < 2)
            {
                Thread.Sleep(100);
                client = windows.GetClientBounds(window);
                bounds = windows.GetWindowBounds(window);
                extended = windows.GetExtendedFrameBounds(window) ?? bounds;
            }
        }

        throw new InvalidOperationException("Windows could not stabilize the Roblox capture surface.");
    }

    public IReadOnlyList<CapturedGrayRegion> CaptureDetectorRegions(
        RobloxWindow window,
        IReadOnlyList<PixelRect> regions)
    {
        return CaptureRgbRegions(window, regions)
            .Select(capture => new CapturedGrayRegion(capture.Region, RgbGrayConverter.Convert(capture.Image)))
            .ToArray();
    }

    public IReadOnlyList<CapturedRgbRegion> CaptureRgbRegions(
        RobloxWindow window,
        IReadOnlyList<PixelRect> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        nint handle = windows.Revalidate(window);
        ClientBounds client = windows.GetClientBounds(window);
        WindowBounds bounds = windows.GetWindowBounds(window);
        WindowBounds extended = windows.GetExtendedFrameBounds(window) ?? bounds;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                IReadOnlyList<RgbImage> images = _capture.CaptureClientRegions(
                    handle,
                    window.ProcessId,
                    client,
                    bounds,
                    extended,
                    regions);
                return images
                    .Select((image, index) => new CapturedRgbRegion(regions[index], image))
                    .ToArray();
            }
            catch (CaptureSurfaceChangedException) when (attempt < 2)
            {
                Thread.Sleep(100);
                client = windows.GetClientBounds(window);
                bounds = windows.GetWindowBounds(window);
                extended = windows.GetExtendedFrameBounds(window) ?? bounds;
            }
        }

        throw new InvalidOperationException("Windows could not stabilize the Roblox detector capture surface.");
    }

    public void Dispose() => _capture.Dispose();

    internal static CaptureFrameDiagnostics AnalyzeFrame(RgbImage image)
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
