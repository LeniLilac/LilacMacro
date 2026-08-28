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
                    _capture.LastFrameDiagnostics ?? CaptureFrameAnalyzer.Analyze(image));
            }
            catch (CaptureSurfaceChangedException) when (attempt < 2)
            {
                Thread.Sleep(100);
                handle = windows.Revalidate(window);
                client = windows.GetClientBounds(window);
                bounds = windows.GetWindowBounds(window);
                extended = windows.GetExtendedFrameBounds(window) ?? bounds;
            }
            catch (CaptureSurfaceChangedException error)
            {
                throw new InvalidOperationException(
                    "Windows could not stabilize the Roblox capture surface after three bounded attempts.",
                    error);
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
                handle = windows.Revalidate(window);
                client = windows.GetClientBounds(window);
                bounds = windows.GetWindowBounds(window);
                extended = windows.GetExtendedFrameBounds(window) ?? bounds;
            }
            catch (CaptureSurfaceChangedException error)
            {
                throw new InvalidOperationException(
                    "Windows could not stabilize the Roblox detector capture surface after three bounded attempts.",
                    error);
            }
        }

        throw new InvalidOperationException("Windows could not stabilize the Roblox detector capture surface.");
    }

    public void Dispose() => _capture.Dispose();

}
