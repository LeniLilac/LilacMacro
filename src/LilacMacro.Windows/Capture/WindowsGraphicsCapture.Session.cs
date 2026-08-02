using System.Runtime.InteropServices;
using LilacMacro.Core.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LilacMacro.Windows.Capture;

internal sealed partial class WindowsGraphicsCapture
{
    private sealed class CaptureSession : IDisposable
    {
        private readonly FrameArrivalGate _arrival = new();
        private readonly nint _window;
        private readonly ClientBounds _client;
        private readonly WindowBounds _windowBounds;
        private readonly WindowBounds _extendedBounds;
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly IDirect3DDevice _winRtDevice;
        private readonly GraphicsCaptureItem _item;
        private readonly Direct3D11CaptureFramePool _framePool;
        private readonly GraphicsCaptureSession _captureSession;
        private readonly ScreenRegion _clientCrop;
        private readonly int _surfaceWidth;
        private readonly int _surfaceHeight;
        private bool _hasCaptured;
        private bool _disposed;

        private CaptureSession(
            nint window,
            ClientBounds client,
            WindowBounds windowBounds,
            WindowBounds extendedBounds,
            ID3D11Device device,
            ID3D11DeviceContext context,
            IDirect3DDevice winRtDevice,
            GraphicsCaptureItem item,
            Direct3D11CaptureFramePool framePool,
            GraphicsCaptureSession captureSession,
            ScreenRegion clientCrop,
            int surfaceWidth,
            int surfaceHeight)
        {
            _window = window;
            _client = client;
            _windowBounds = windowBounds;
            _extendedBounds = extendedBounds;
            _device = device;
            _context = context;
            _winRtDevice = winRtDevice;
            _item = item;
            _framePool = framePool;
            _captureSession = captureSession;
            _clientCrop = clientCrop;
            _surfaceWidth = surfaceWidth;
            _surfaceHeight = surfaceHeight;
            _framePool.FrameArrived += FrameArrived;
            _captureSession.StartCapture();
        }

        public static CaptureSession Create(
            nint window,
            ClientBounds client,
            WindowBounds windowBounds,
            WindowBounds extendedBounds)
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                throw new PlatformNotSupportedException("Reliable capture requires Windows 10 version 1903 or later.");
            }

            (ID3D11Device device, ID3D11DeviceContext context) = CreateCaptureDevice();
            IDirect3DDevice winRtDevice = CreateWinRtDevice(device);
            GraphicsCaptureItem item = CreateItem(window);
            SizeInt32 size = item.Size;
            ScreenRegion crop = ResolveClientCrop(size.Width, size.Height, client, windowBounds, extendedBounds);
            Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winRtDevice,
                DirectXPixelFormat.R16G16B16A16Float,
                2,
                size);
            GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            return new CaptureSession(
                window,
                client,
                windowBounds,
                extendedBounds,
                device,
                context,
                winRtDevice,
                item,
                pool,
                session,
                crop,
                size.Width,
                size.Height);
        }

        public bool Matches(
            nint window,
            ClientBounds client,
            WindowBounds windowBounds,
            WindowBounds extendedBounds) =>
            !_disposed &&
            _window == window &&
            client.Width == _client.Width &&
            client.Height == _client.Height &&
            client.X - extendedBounds.X == _client.X - _extendedBounds.X &&
            client.Y - extendedBounds.Y == _client.Y - _extendedBounds.Y &&
            windowBounds.Width == _windowBounds.Width &&
            windowBounds.Height == _windowBounds.Height &&
            extendedBounds.Width == _extendedBounds.Width &&
            extendedBounds.Height == _extendedBounds.Height;

        public RgbImage Capture()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            FrameQueue.DiscardAll(_framePool.TryGetNextFrame);
            long targetGeneration = _arrival.Generation + 1;
            int timeout = _hasCaptured ? 1500 : 3500;
            if (!_arrival.WaitForGeneration(targetGeneration, timeout))
            {
                throw new TimeoutException($"Windows did not provide a fresh Roblox frame within {timeout} milliseconds.");
            }

            using Direct3D11CaptureFrame? frame = FrameQueue.TakeLatest(_framePool.TryGetNextFrame);
            if (frame is null) throw new TimeoutException("Windows announced a frame but returned no capture surface.");
            if (frame.ContentSize.Width != _surfaceWidth || frame.ContentSize.Height != _surfaceHeight)
            {
                throw new CaptureSurfaceChangedException(
                    _surfaceWidth,
                    _surfaceHeight,
                    frame.ContentSize.Width,
                    frame.ContentSize.Height);
            }

            using ID3D11Texture2D source = GetTexture(frame.Surface);
            byte[] pixels = ReadTexturePixels(source);
            _hasCaptured = true;
            return CaptureSurfaceConverter.ConvertScRgbRgba16ToRgb(
                pixels,
                _surfaceWidth,
                _surfaceHeight,
                _clientCrop);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _framePool.FrameArrived -= FrameArrived;
            _captureSession.Dispose();
            _framePool.Dispose();
            _arrival.Dispose();
            (_winRtDevice as IDisposable)?.Dispose();
            _context.Dispose();
            _device.Dispose();
        }

        private void FrameArrived(Direct3D11CaptureFramePool sender, object args) => _arrival.Notify();

        private byte[] ReadTexturePixels(ID3D11Texture2D source)
        {
            Texture2DDescription description = source.Description;
            if (description.Format != Format.R16G16B16A16_Float ||
                description.Width != _surfaceWidth ||
                description.Height != _surfaceHeight)
            {
                throw new InvalidOperationException(
                    $"Windows returned an unexpected capture texture ({description.Format}, {description.Width} × {description.Height}).");
            }

            Texture2DDescription stagingDescription = new(
                description.Format,
                description.Width,
                description.Height,
                description.ArraySize,
                description.MipLevels,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read,
                description.SampleDescription.Count,
                description.SampleDescription.Quality,
                ResourceOptionFlags.None);
            using ID3D11Texture2D staging = _device.CreateTexture2D(stagingDescription);
            _context.CopyResource(staging, source);
            MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read);
            try
            {
                int rowBytes = checked(_surfaceWidth * 8);
                byte[] pixels = new byte[checked(rowBytes * _surfaceHeight)];
                for (int row = 0; row < _surfaceHeight; row++)
                {
                    nint rowPointer = IntPtr.Add(mapped.DataPointer, checked((int)(row * mapped.RowPitch)));
                    Marshal.Copy(rowPointer, pixels, row * rowBytes, rowBytes);
                }
                return pixels;
            }
            finally
            {
                _context.Unmap(staging, 0);
            }
        }
    }
}
