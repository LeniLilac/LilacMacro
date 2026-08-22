using System.ComponentModel;
using System.Runtime.InteropServices;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LilacMacro.Windows.Capture;

internal sealed partial class WindowsGraphicsCapture : IDisposable
{
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Direct3D11Texture2DId = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private readonly object _gate = new();
    private CaptureSession? _active;
    private bool _disposed;

    public CaptureColorDiagnostics? LastColorDiagnostics { get; private set; }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);

        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface(in Guid iid);
    }

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    public RgbImage CaptureClient(
        nint window,
        ClientBounds client,
        WindowBounds windowBounds,
        WindowBounds extendedFrameBounds)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (_active is null || !_active.Matches(window, client, windowBounds, extendedFrameBounds))
                    {
                        _active?.Dispose();
                        _active = CaptureSession.Create(window, client, windowBounds, extendedFrameBounds);
                    }
                    RgbImage image = _active.Capture();
                    LastColorDiagnostics = _active.ColorDiagnostics;
                    return image;
                }
                catch (Exception error) when (ShouldRebuildCaptureSession(error, attempt))
                {
                    _active?.Dispose();
                    _active = null;
                }
                catch (CaptureSurfaceChangedException)
                {
                    _active?.Dispose();
                    _active = null;
                    throw;
                }
            }
            throw new TimeoutException("Windows did not provide a fresh Roblox frame after rebuilding the capture session.");
        }
    }

    public IReadOnlyList<RgbImage> CaptureClientRegions(
        nint window,
        ClientBounds client,
        WindowBounds windowBounds,
        WindowBounds extendedFrameBounds,
        IReadOnlyList<PixelRect> regions)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (_active is null || !_active.Matches(window, client, windowBounds, extendedFrameBounds))
                    {
                        _active?.Dispose();
                        _active = CaptureSession.Create(window, client, windowBounds, extendedFrameBounds);
                    }
                    IReadOnlyList<RgbImage> images = _active.CaptureRegions(regions);
                    LastColorDiagnostics = _active.ColorDiagnostics;
                    return images;
                }
                catch (Exception error) when (ShouldRebuildCaptureSession(error, attempt))
                {
                    _active?.Dispose();
                    _active = null;
                }
                catch (CaptureSurfaceChangedException)
                {
                    _active?.Dispose();
                    _active = null;
                    throw;
                }
            }
            throw new TimeoutException("Windows did not provide a fresh Roblox frame after rebuilding the capture session.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active?.Dispose();
            _active = null;
        }
    }

    internal static bool ShouldRebuildCaptureSession(Exception error, int attempt) =>
        attempt == 0 && error is TimeoutException or ObjectDisposedException;

    internal static ScreenRegion ResolveClientCrop(
        int surfaceWidth,
        int surfaceHeight,
        ClientBounds client,
        WindowBounds windowBounds,
        WindowBounds extendedFrameBounds)
    {
        if (surfaceWidth == client.Width && surfaceHeight == client.Height)
        {
            return new ScreenRegion(0, 0, client.Width, client.Height);
        }

        foreach (WindowBounds candidate in new[] { extendedFrameBounds, windowBounds })
        {
            if (Math.Abs(candidate.Width - surfaceWidth) > 2 || Math.Abs(candidate.Height - surfaceHeight) > 2) continue;
            int x = client.X - candidate.X;
            int y = client.Y - candidate.Y;
            if (x >= 0 && y >= 0 && x + client.Width <= surfaceWidth && y + client.Height <= surfaceHeight)
            {
                return new ScreenRegion(x, y, client.Width, client.Height);
            }
        }

        throw new InvalidOperationException(
            $"Windows captured a {surfaceWidth} × {surfaceHeight} surface, but the {client.Width} × {client.Height} Roblox client could not be mapped into it.");
    }

    private static GraphicsCaptureItem CreateItem(nint window)
    {
        IGraphicsCaptureItemInterop interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        nint itemPointer = interop.CreateForWindow(window, GraphicsCaptureItemId);
        if (itemPointer == nint.Zero) throw new Win32Exception("Windows could not create a Roblox capture target.");
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    private static IDirect3DDevice CreateWinRtDevice(ID3D11Device device)
    {
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        int result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out nint pointer);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static (ID3D11Device Device, ID3D11DeviceContext Context) CreateCaptureDevice()
    {
        FeatureLevel[] levels =
        [
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        ];
        try
        {
            var result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out ID3D11Device device,
                out ID3D11DeviceContext context);
            result.CheckError();
            return (device, context);
        }
        catch
        {
            var result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                levels,
                out ID3D11Device device,
                out ID3D11DeviceContext context);
            result.CheckError();
            return (device, context);
        }
    }

    private static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        IDirect3DDxgiInterfaceAccess access = surface.As<IDirect3DDxgiInterfaceAccess>();
        nint pointer = access.GetInterface(Direct3D11Texture2DId);
        if (pointer == nint.Zero) throw new InvalidOperationException("Windows returned a frame without a Direct3D texture.");
        return new ID3D11Texture2D(pointer);
    }
}
