using LilacMacro.Core.Imaging;

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
                RgbImage image = _capture.CaptureClient(handle, client, bounds, extended);
                return new CapturedPng(image.Size, PngEncoder.Encode(image));
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

    public void Dispose() => _capture.Dispose();
}
