using System.Runtime.InteropServices;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LilacMacro.Windows.Capture;

internal sealed partial class WindowsGraphicsCapture
{
    private sealed class CaptureSession : IDisposable
    {
        private readonly object _lifecycleGate = new();
        private readonly FrameArrivalGate _arrival = new();
        private readonly nint _window;
        private readonly int _processId;
        private readonly ClientBounds _client;
        private readonly WindowBounds _windowBounds;
        private readonly WindowBounds _extendedBounds;
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly IDirect3DDevice _winRtDevice;
        private readonly GraphicsCaptureItem _item;
        private readonly Direct3D11CaptureFramePool _framePool;
        private readonly GraphicsCaptureSession _captureSession;
        private readonly CaptureSurfaceFormat _surfaceFormat;
        private ScreenRegion _clientCrop;
        private int _surfaceWidth;
        private int _surfaceHeight;
        private CaptureColorContext _colorContext;
        private long _colorContextRefreshTicks;
        private int _processingCallbacks;
        private bool _hasCaptured;
        private bool _disposed;

        private CaptureSession(
            nint window,
            int processId,
            ClientBounds client,
            WindowBounds windowBounds,
            WindowBounds extendedBounds,
            ID3D11Device device,
            ID3D11DeviceContext context,
            IDirect3DDevice winRtDevice,
            GraphicsCaptureItem item,
            Direct3D11CaptureFramePool framePool,
            GraphicsCaptureSession captureSession,
            CaptureSurfaceFormat surfaceFormat,
            ScreenRegion clientCrop,
            int surfaceWidth,
            int surfaceHeight,
            CaptureColorContext colorContext)
        {
            _window = window;
            _processId = processId;
            _client = client;
            _windowBounds = windowBounds;
            _extendedBounds = extendedBounds;
            _device = device;
            _context = context;
            _winRtDevice = winRtDevice;
            _item = item;
            _framePool = framePool;
            _captureSession = captureSession;
            _surfaceFormat = surfaceFormat;
            _clientCrop = clientCrop;
            _surfaceWidth = surfaceWidth;
            _surfaceHeight = surfaceHeight;
            _colorContext = colorContext;
            _colorContextRefreshTicks = Environment.TickCount64;
            _framePool.FrameArrived += FrameArrived;
            _captureSession.StartCapture();
        }

        public static CaptureSession Create(
            nint window,
            int processId,
            ClientBounds client,
            WindowBounds windowBounds,
            WindowBounds extendedBounds,
            CaptureSurfaceFormat surfaceFormat)
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
                ToDirectXPixelFormat(surfaceFormat),
                2,
                size);
            GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            return new CaptureSession(
                window,
                processId,
                client,
                windowBounds,
                extendedBounds,
                device,
                context,
                winRtDevice,
                item,
                pool,
                session,
                surfaceFormat,
                crop,
                size.Width,
                size.Height,
                DisplayColorContextProvider.GetForWindow(window));
        }

        public bool Matches(
            nint window,
            int processId,
            ClientBounds client,
            WindowBounds windowBounds,
            WindowBounds extendedBounds) =>
            !_disposed &&
            _window == window &&
            _processId == processId &&
            client.Width == _client.Width &&
            client.Height == _client.Height &&
            client.X - extendedBounds.X == _client.X - _extendedBounds.X &&
            client.Y - extendedBounds.Y == _client.Y - _extendedBounds.Y &&
            windowBounds.Width == _windowBounds.Width &&
            windowBounds.Height == _windowBounds.Height &&
            extendedBounds.Width == _extendedBounds.Width &&
            extendedBounds.Height == _extendedBounds.Height;

        public CaptureColorDiagnostics ColorDiagnostics => _surfaceFormat == CaptureSurfaceFormat.ScRgbFloat
            ? _colorContext.ToDiagnostics()
            : new CaptureColorDiagnostics(
                "B8G8R8A8UIntNormalized",
                _colorContext.OutputColorSpace,
                _colorContext.AdvancedColorActive,
                _colorContext.SdrWhiteLevelNits,
                _colorContext.DisplayMaxLuminanceNits,
                1f,
                _colorContext.AdvancedColorActive
                    ? "bgra8-compatibility-fallback"
                    : "bgra8-sdr-compositor-output",
                _colorContext.UsedSdrWhiteFallback);

        public RgbImage Capture()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using Direct3D11CaptureFrame frame = TakeFreshFrame();
            using ID3D11Texture2D source = GetTexture(frame.Surface);
            byte[] pixels = ReadTextureRegionPixels(source, _clientCrop);
            RefreshColorContextIfNeeded();
            return ConvertSurface(
                pixels,
                _client.Width,
                _client.Height,
                new ScreenRegion(0, 0, _client.Width, _client.Height));
        }

        public IReadOnlyList<RgbImage> CaptureRegions(IReadOnlyList<PixelRect> regions)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CaptureAtlasLayout layout = CaptureAtlasLayout.Create(_client.Width, _client.Height, regions);
            using Direct3D11CaptureFrame frame = TakeFreshFrame();
            using ID3D11Texture2D source = GetTexture(frame.Surface);
            byte[] pixels = ReadTextureAtlasPixels(source, layout);
            RefreshColorContextIfNeeded();

            RgbImage[] images = new RgbImage[layout.Entries.Count];
            foreach (CaptureAtlasEntry entry in layout.Entries)
            {
                images[entry.RequestIndex] = ConvertSurface(
                    pixels,
                    layout.Width,
                    layout.Height,
                    entry.Atlas);
            }
            return images;
        }

        public void Dispose()
        {
            lock (_lifecycleGate)
            {
                if (_disposed) return;
                _disposed = true;
                _framePool.FrameArrived -= FrameArrived;
            }
            _captureSession.Dispose();
            _framePool.Dispose();
            _arrival.Wake();
            bool callbacksDrained = SpinWait.SpinUntil(
                () => Volatile.Read(ref _processingCallbacks) == 0,
                TimeSpan.FromSeconds(2));
            if (!callbacksDrained)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    SpinWait.SpinUntil(() => Volatile.Read(ref _processingCallbacks) == 0);
                    DisposeCaptureResources();
                });
                return;
            }

            DisposeCaptureResources();
        }

        private void DisposeCaptureResources()
        {
            _arrival.Dispose();
            (_winRtDevice as IDisposable)?.Dispose();
            _context.Dispose();
            _device.Dispose();
        }

        private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (_lifecycleGate)
            {
                if (_disposed) return;
                Interlocked.Increment(ref _processingCallbacks);
            }
            try
            {
                // Windows 10 can reject apartment-bound WinRT frame access on this
                // free-threaded callback. Only signal here; consume frames on Capture().
                _arrival.Notify();
            }
            finally
            {
                Interlocked.Decrement(ref _processingCallbacks);
            }
        }

        private Direct3D11CaptureFrame TakeFreshFrame()
        {
            FrameQueue.DiscardAll(_framePool.TryGetNextFrame);
            long targetGeneration = _arrival.Generation + 1;
            int timeout = _hasCaptured ? 1500 : 3500;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            int surfaceRecreates = 0;
            bool stabilizingSurface = false;
            while (DateTime.UtcNow < deadline)
            {
                int remaining = Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                if (!_arrival.WaitForGeneration(targetGeneration, remaining)) break;
                long availableGeneration = _arrival.Generation;
                Direct3D11CaptureFrame? frame = FrameQueue.TakeLatest(_framePool.TryGetNextFrame);
                if (frame is null)
                {
                    targetGeneration = availableGeneration + 1;
                    continue;
                }

                SizeInt32 contentSize;
                try
                {
                    contentSize = frame.ContentSize;
                }
                catch
                {
                    frame.Dispose();
                    throw;
                }

                if (contentSize.Width == _surfaceWidth && contentSize.Height == _surfaceHeight)
                {
                    _hasCaptured = true;
                    return frame;
                }

                frame.Dispose();
                if (!ShouldRecreateSurface(surfaceRecreates))
                {
                    throw new CaptureSurfaceChangedException(
                        _surfaceWidth,
                        _surfaceHeight,
                        contentSize.Width,
                        contentSize.Height);
                }

                surfaceRecreates++;
                RecreateSurface(contentSize.Width, contentSize.Height);
                FrameQueue.DiscardAll(_framePool.TryGetNextFrame);
                targetGeneration = _arrival.Generation + 1;
                if (!stabilizingSurface)
                {
                    deadline = DateTime.UtcNow.AddMilliseconds(3500);
                    stabilizingSurface = true;
                }
            }

            throw new TimeoutException($"Windows did not provide a fresh Roblox frame within {timeout} milliseconds.");
        }

        private void RecreateSurface(int surfaceWidth, int surfaceHeight)
        {
            ScreenRegion clientCrop;
            try
            {
                clientCrop = ResolveClientCrop(
                    surfaceWidth,
                    surfaceHeight,
                    _client,
                    _windowBounds,
                    _extendedBounds);
            }
            catch (InvalidOperationException error)
            {
                throw new CaptureSurfaceChangedException(
                    _surfaceWidth,
                    _surfaceHeight,
                    surfaceWidth,
                    surfaceHeight,
                    error);
            }

            _framePool.Recreate(
                _winRtDevice,
                ToDirectXPixelFormat(_surfaceFormat),
                2,
                new SizeInt32(surfaceWidth, surfaceHeight));
            _clientCrop = clientCrop;
            _surfaceWidth = surfaceWidth;
            _surfaceHeight = surfaceHeight;
            _hasCaptured = false;
        }

        private void RefreshColorContextIfNeeded()
        {
            long now = Environment.TickCount64;
            if (now - _colorContextRefreshTicks < 1000) return;
            _colorContext = DisplayColorContextProvider.GetForWindow(_window);
            _colorContextRefreshTicks = now;
        }

        private byte[] ReadTextureRegionPixels(ID3D11Texture2D source, ScreenRegion region)
        {
            using ID3D11Texture2D compact = CaptureTextureFactory.Create(
                _device,
                region.Width,
                region.Height,
                ToDxgiFormat(_surfaceFormat));
            Box sourceBox = new(region.X, region.Y, 0, region.Right, region.Bottom, 1);
            _context.CopySubresourceRegion(compact, 0, 0, 0, 0, source, 0, sourceBox);
            return ReadTexturePixels(compact, region.Width, region.Height);
        }

        private byte[] ReadTextureAtlasPixels(ID3D11Texture2D source, CaptureAtlasLayout layout)
        {
            using ID3D11Texture2D atlas = CaptureTextureFactory.Create(
                _device,
                layout.Width,
                layout.Height,
                ToDxgiFormat(_surfaceFormat));
            foreach (CaptureAtlasEntry entry in layout.Entries)
            {
                ScreenRegion sourceRegion = new(
                    checked(_clientCrop.X + entry.Source.X),
                    checked(_clientCrop.Y + entry.Source.Y),
                    entry.Source.Width,
                    entry.Source.Height);
                Box sourceBox = new(
                    sourceRegion.X,
                    sourceRegion.Y,
                    0,
                    sourceRegion.Right,
                    sourceRegion.Bottom,
                    1);
                _context.CopySubresourceRegion(
                    atlas,
                    0,
                    checked((uint)entry.Atlas.X),
                    checked((uint)entry.Atlas.Y),
                    0,
                    source,
                    0,
                    sourceBox);
            }
            return ReadTexturePixels(atlas, layout.Width, layout.Height);
        }

        private byte[] ReadTexturePixels(ID3D11Texture2D source, int width, int height)
        {
            Texture2DDescription description = source.Description;
            Format expectedFormat = ToDxgiFormat(_surfaceFormat);
            if (description.Format != expectedFormat ||
                description.Width != width ||
                description.Height != height)
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
                int rowBytes = checked(width * BytesPerPixel(_surfaceFormat));
                byte[] pixels = new byte[checked(rowBytes * height)];
                for (int row = 0; row < height; row++)
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

        private RgbImage ConvertSurface(
            byte[] pixels,
            int surfaceWidth,
            int surfaceHeight,
            ScreenRegion crop) =>
            _surfaceFormat == CaptureSurfaceFormat.ScRgbFloat
                ? CaptureSurfaceConverter.ConvertScRgbRgba16ToRgb(
                    pixels,
                    surfaceWidth,
                    surfaceHeight,
                    crop,
                    _colorContext)
                : CaptureSurfaceConverter.ConvertBgra8ToRgb(
                    pixels,
                    surfaceWidth,
                    surfaceHeight,
                    crop);

        private static DirectXPixelFormat ToDirectXPixelFormat(CaptureSurfaceFormat format) => format switch
        {
            CaptureSurfaceFormat.ScRgbFloat => DirectXPixelFormat.R16G16B16A16Float,
            CaptureSurfaceFormat.Bgra8 => DirectXPixelFormat.B8G8R8A8UIntNormalized,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        private static Format ToDxgiFormat(CaptureSurfaceFormat format) => format switch
        {
            CaptureSurfaceFormat.ScRgbFloat => Format.R16G16B16A16_Float,
            CaptureSurfaceFormat.Bgra8 => Format.B8G8R8A8_UNorm,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        private static int BytesPerPixel(CaptureSurfaceFormat format) => format switch
        {
            CaptureSurfaceFormat.ScRgbFloat => 8,
            CaptureSurfaceFormat.Bgra8 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }
}
