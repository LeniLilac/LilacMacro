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
    internal const int MaximumCaptureAttempts = 3;
    internal const int MaximumSurfaceRecreateAttempts = 4;
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Direct3D11Texture2DId = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private readonly object _gate = new();
    private CaptureSession? _active;
    private CaptureColorDiagnostics? _lastSessionDiagnostics;
    private CaptureSurfaceFormat _surfaceFormat = CaptureSurfaceFormat.Bgra8;
    private CaptureColorContext? _fixedColorContext;
    private CaptureExposureProbeDiagnostics _exposureProbe = CaptureExposureProbeDiagnostics.NotNeeded;
    private readonly Queue<CaptureFrameDiagnostics> _exposureObservations = new();
    private nint _targetWindow;
    private int _targetProcessId;
    private int _exposureProbeAttempts;
    private long _nextExposureProbeTicks;
    private bool _disposed;

    public CaptureColorDiagnostics? LastColorDiagnostics { get; private set; }

    public CaptureFrameDiagnostics? LastFrameDiagnostics { get; private set; }

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
        int processId,
        ClientBounds client,
        WindowBounds windowBounds,
        WindowBounds extendedFrameBounds)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PrepareTarget(window, processId);
            Exception? lastRecoveryError = null;
            for (int attempt = 0; attempt < MaximumCaptureAttempts; attempt++)
            {
                try
                {
                    if (_active is null || !_active.Matches(
                            window,
                            processId,
                            client,
                            windowBounds,
                            extendedFrameBounds))
                    {
                        _active?.Dispose();
                        _active = CaptureSession.Create(
                            window,
                            processId,
                            client,
                            windowBounds,
                            extendedFrameBounds,
                            _surfaceFormat,
                            _fixedColorContext);
                    }
                    RgbImage image = _active.Capture();
                    _lastSessionDiagnostics = _active.ColorDiagnostics;
                    CaptureFrameDiagnostics frameDiagnostics = CaptureFrameAnalyzer.Analyze(image);
                    image = ProbeExposureIfNeeded(
                        image,
                        frameDiagnostics,
                        window,
                        processId,
                        client,
                        windowBounds,
                        extendedFrameBounds);
                    LastFrameDiagnostics = CaptureFrameAnalyzer.Analyze(image);
                    LastColorDiagnostics = DecorateDiagnostics(_lastSessionDiagnostics);
                    return image;
                }
                catch (Exception error) when (IsRecoverableCaptureFailure(error))
                {
                    lastRecoveryError = error;
                    _active?.Dispose();
                    _active = null;
                    if (!ShouldRetryCapture(error, attempt)) break;
                    _surfaceFormat = SelectRetryFormat(_surfaceFormat, attempt);
                }
                catch (CaptureSurfaceChangedException)
                {
                    _active?.Dispose();
                    _active = null;
                    throw;
                }
            }
            throw CreateUnavailableException(lastRecoveryError);
        }
    }

    public IReadOnlyList<RgbImage> CaptureClientRegions(
        nint window,
        int processId,
        ClientBounds client,
        WindowBounds windowBounds,
        WindowBounds extendedFrameBounds,
        IReadOnlyList<PixelRect> regions)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PrepareTarget(window, processId);
            Exception? lastRecoveryError = null;
            for (int attempt = 0; attempt < MaximumCaptureAttempts; attempt++)
            {
                try
                {
                    if (_active is null || !_active.Matches(
                            window,
                            processId,
                            client,
                            windowBounds,
                            extendedFrameBounds))
                    {
                        _active?.Dispose();
                        _active = CaptureSession.Create(
                            window,
                            processId,
                            client,
                            windowBounds,
                            extendedFrameBounds,
                            _surfaceFormat,
                            _fixedColorContext);
                    }
                    IReadOnlyList<RgbImage> images = _active.CaptureRegions(regions);
                    _lastSessionDiagnostics = _active.ColorDiagnostics;
                    LastFrameDiagnostics = null;
                    LastColorDiagnostics = DecorateDiagnostics(_lastSessionDiagnostics);
                    return images;
                }
                catch (Exception error) when (IsRecoverableCaptureFailure(error))
                {
                    lastRecoveryError = error;
                    _active?.Dispose();
                    _active = null;
                    if (!ShouldRetryCapture(error, attempt)) break;
                    _surfaceFormat = SelectRetryFormat(_surfaceFormat, attempt);
                }
                catch (CaptureSurfaceChangedException)
                {
                    _active?.Dispose();
                    _active = null;
                    throw;
                }
            }
            throw CreateUnavailableException(lastRecoveryError);
        }
    }

    private RgbImage ProbeExposureIfNeeded(
        RgbImage source,
        CaptureFrameDiagnostics sourceDiagnostics,
        nint window,
        int processId,
        ClientBounds client,
        WindowBounds windowBounds,
        WindowBounds extendedFrameBounds)
    {
        if (_surfaceFormat != CaptureSurfaceFormat.Bgra8 || _fixedColorContext is not null)
            return source;

        if (!ObserveExposure(sourceDiagnostics) ||
            _exposureProbeAttempts >= CaptureExposurePolicy.MaximumProbeAttempts ||
            Environment.TickCount64 < _nextExposureProbeTicks)
            return source;

        _exposureProbeAttempts++;
        _exposureObservations.Clear();
        CaptureColorContext forcedContext = CreateExposureFallbackContext(
            _lastSessionDiagnostics ?? CaptureColorContext.StandardSdr.ToDiagnostics());
        try
        {
            _active?.Dispose();
            _active = CaptureSession.Create(
                window,
                processId,
                client,
                windowBounds,
                extendedFrameBounds,
                CaptureSurfaceFormat.ScRgbFloat,
                forcedContext);
            RgbImage candidate = _active.Capture();
            CaptureFrameDiagnostics candidateDiagnostics = CaptureFrameAnalyzer.Analyze(candidate);
            double correlation = CaptureExposurePolicy.MeasureStructuralCorrelation(source, candidate);
            if (CaptureExposurePolicy.ShouldAcceptFallback(
                    sourceDiagnostics,
                    candidateDiagnostics,
                    correlation))
            {
                _surfaceFormat = CaptureSurfaceFormat.ScRgbFloat;
                _fixedColorContext = forcedContext;
                _lastSessionDiagnostics = _active.ColorDiagnostics;
                _exposureProbe = new(
                    "forced-scrgb-active",
                    sourceDiagnostics.ClippedPixelPercent,
                    candidateDiagnostics.ClippedPixelPercent,
                    correlation,
                    null);
                return candidate;
            }

            _exposureProbe = new(
                _exposureProbeAttempts < CaptureExposurePolicy.MaximumProbeAttempts
                    ? "candidate-rejected-retry-pending"
                    : "candidate-rejected",
                sourceDiagnostics.ClippedPixelPercent,
                candidateDiagnostics.ClippedPixelPercent,
                correlation,
                null);
        }
        catch (Exception error)
        {
            _exposureProbe = new(
                _exposureProbeAttempts < CaptureExposurePolicy.MaximumProbeAttempts
                    ? "probe-failed-retry-pending"
                    : "probe-failed",
                sourceDiagnostics.ClippedPixelPercent,
                null,
                null,
                error.GetType().Name);
        }

        _active?.Dispose();
        _active = null;
        _surfaceFormat = CaptureSurfaceFormat.Bgra8;
        _fixedColorContext = null;
        _nextExposureProbeTicks = Environment.TickCount64 +
            (long)CaptureExposurePolicy.RetryDelay.TotalMilliseconds;
        return source;
    }

    private bool ObserveExposure(CaptureFrameDiagnostics observation)
    {
        if (!CaptureExposurePolicy.IsEligibleObservation(observation)) return false;
        _exposureObservations.Enqueue(observation);
        while (_exposureObservations.Count > CaptureExposurePolicy.ObservationWindowSize)
            _exposureObservations.Dequeue();
        return CaptureExposurePolicy.IsSuspiciousWindow(_exposureObservations);
    }

    private CaptureColorDiagnostics? DecorateDiagnostics(CaptureColorDiagnostics? diagnostics) =>
        diagnostics is null
            ? null
            : diagnostics with
            {
                ExposureProbeOutcome = _exposureProbe.Outcome,
                ExposureSourceClippedPixelPercent = _exposureProbe.SourceClippedPixelPercent,
                ExposureCandidateClippedPixelPercent = _exposureProbe.CandidateClippedPixelPercent,
                ExposureStructuralCorrelation = _exposureProbe.StructuralCorrelation,
                ExposureProbeFailureType = _exposureProbe.FailureType,
            };

    internal static CaptureColorContext CreateExposureFallbackContext(
        CaptureColorDiagnostics source) =>
        new(
            true,
            CaptureColorContext.AdvancedColorFallbackWhiteNits,
            Math.Max(1000f, source.DisplayMaxLuminanceNits),
            source.OutputColorSpace,
            "bgra8-exposure-anomaly+forced-scrgb",
            usedSdrWhiteFallback: true);

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

    internal static bool ShouldRetryCapture(Exception error, int attempt) =>
        attempt < MaximumCaptureAttempts - 1 &&
        IsRecoverableCaptureFailure(error);

    internal static bool ShouldRecreateSurface(int completedAttempts) =>
        completedAttempts >= 0 && completedAttempts < MaximumSurfaceRecreateAttempts;

    private static bool IsRecoverableCaptureFailure(Exception error) =>
        error is TimeoutException or ObjectDisposedException;

    internal static CaptureSurfaceFormat SelectRetryFormat(
        CaptureSurfaceFormat current,
        int completedAttempt) =>
        current == CaptureSurfaceFormat.ScRgbFloat && completedAttempt >= 1
            ? CaptureSurfaceFormat.Bgra8
            : current;

    internal static CaptureSurfaceFormat SelectInitialFormat(CaptureColorContext colorContext) =>
        colorContext.AdvancedColorActive
            ? CaptureSurfaceFormat.ScRgbFloat
            : CaptureSurfaceFormat.Bgra8;

    internal static bool IsSameTarget(
        nint currentWindow,
        int currentProcessId,
        nint nextWindow,
        int nextProcessId) =>
        currentWindow == nextWindow && currentProcessId == nextProcessId;

    private void PrepareTarget(nint window, int processId)
    {
        if (IsSameTarget(_targetWindow, _targetProcessId, window, processId)) return;
        _active?.Dispose();
        _active = null;
        _targetWindow = window;
        _targetProcessId = processId;
        CaptureColorContext colorContext = DisplayColorContextProvider.GetForWindow(window);
        _surfaceFormat = SelectInitialFormat(colorContext);
        _fixedColorContext = null;
        _lastSessionDiagnostics = null;
        _exposureProbe = CaptureExposureProbeDiagnostics.NotNeeded;
        _exposureObservations.Clear();
        _exposureProbeAttempts = 0;
        _nextExposureProbeTicks = 0;
        LastColorDiagnostics = null;
        LastFrameDiagnostics = null;
    }

    private RobloxCaptureUnavailableException CreateUnavailableException(Exception? innerException) =>
        new(
            $"Windows Graphics Capture did not provide a usable Roblox frame after " +
            $"{MaximumCaptureAttempts} bounded attempts; final format: {_surfaceFormat}.",
            innerException ?? new TimeoutException("No capture frame was returned."));

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

internal enum CaptureSurfaceFormat
{
    ScRgbFloat,
    Bgra8,
}

internal readonly record struct CaptureExposureProbeDiagnostics(
    string Outcome,
    double? SourceClippedPixelPercent,
    double? CandidateClippedPixelPercent,
    double? StructuralCorrelation,
    string? FailureType)
{
    public static CaptureExposureProbeDiagnostics NotNeeded { get; } =
        new("not-needed", null, null, null, null);
}
