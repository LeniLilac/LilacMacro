using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Runtime;

internal sealed class PlacementSetupTestService : IDisposable
{
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly WorkspaceController _workspace;
    private readonly OcrRunner _ocr;
    private readonly PlacementPlaybackService _playback;
    private readonly MapPreparationService _mapPreparation;
    private readonly IDisposable _deepDebugFrameCaptureRegistration;
    private bool _initialized;
    private bool _disposed;

    public PlacementSetupTestService(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _workspace = new WorkspaceController(deepDebug);
        _deepDebugFrameCaptureRegistration = deepDebug.RegisterFrameCaptureProvider(
            "main-setup",
            async token =>
            {
                await _workspace.CaptureLiveFrameAsync(
                    DebugWorkflowCatalog.ClientSize,
                    token,
                    "deep-debug-interval");
            });
        _ocr = new OcrRunner(deepDebug) { KeepLoaded = true };
        _playback = new PlacementPlaybackService(_workspace, _ocr);
        _mapPreparation = new MapPreparationService(_workspace);
    }

    public Task<int> RunAsync(
        PlacementSetupDocument document,
        PlacementRouteSetup route,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(route);
        return _deepDebug.RunOperationAsync(
            "placement-setup-test",
            new DeepDebugOperationContext(
                "main-setup",
                new { document.MapId, Route = route.RouteId, Steps = route.Steps.Count }),
            token => RunCoreAsync(document, route, status, token),
            cancellationToken);
    }

    private async Task<int> RunCoreAsync(
        PlacementSetupDocument document,
        PlacementRouteSetup route,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        MacroRuntimeKeySnapshot keys = _ownerState.KeyBindings.Snapshot();
        string device = SelectOcrDevice();
        if (!_initialized)
        {
            await _workspace.InitializeAsync(cancellationToken);
            _initialized = true;
        }
        else
        {
            await _workspace.RefreshWindowAsync(cancellationToken);
        }

        if (_workspace.ObservedClientSize != DebugWorkflowCatalog.ClientSize)
        {
            status?.Invoke("SIZING ROBLOX");
            await _workspace.ApplyClientSizeAsync(DebugWorkflowCatalog.ClientSize, cancellationToken);
        }

        status?.Invoke("ALIGNING CAMERA");
        await _workspace.AlignCameraAsync(
            DebugWorkflowCatalog.ClientSize,
            keys.ShiftLock,
            cancellationToken);
        status?.Invoke("PREPARING MAP POSITION");
        await _mapPreparation.PrepareAsync(
            document.MapId,
            keys.Placement.ReservedVirtualKey,
            status,
            cancellationToken);
        status?.Invoke("PLAYING SETUP");
        return await _playback.RunSetupAsync(
            document,
            route,
            keys.Placement,
            device,
            status,
            cancellationToken);
    }

    private string SelectOcrDevice()
    {
        if (_ocr.IsDeviceReady(OcrRunner.GpuDevice)) return OcrRunner.GpuDevice;
        if (_ocr.IsDeviceReady(OcrRunner.CpuDevice)) return OcrRunner.CpuDevice;
        throw new InvalidOperationException("Set up OCR in Dataset Builder before testing a setup.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _deepDebugFrameCaptureRegistration.Dispose();
        _ocr.Dispose();
        _workspace.Dispose();
    }
}
