using System.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Capture;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Windows;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Workspace;

public sealed partial class WorkspaceController : IDisposable
{
    private readonly AppSettingsStore _settingsStore = new();
    private readonly DatasetStore _datasets = new();
    private readonly RobloxWindowService _windows = new();
    private readonly WorkspaceInputCoordinator _input;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly RobloxCaptureService _capture;
    private readonly DeepDebugSessionService _deepDebug;
    private AppSettings _settings = new();
    private DatasetLocation? _manualCaptureDataset;

    public WorkspaceController(DeepDebugSessionService deepDebug)
    {
        _deepDebug = deepDebug;
        _capture = new RobloxCaptureService(_windows);
        _input = new WorkspaceInputCoordinator(
            _windows,
            new RobloxInputService(_windows),
            _operationGate,
            _deepDebug,
            () => RobloxWindow,
            (window, size) =>
            {
                RobloxWindow = window;
                ObservedClientSize = size;
                Changed?.Invoke(this, EventArgs.Empty);
            });
    }

    public event EventHandler? Changed;

    public RobloxWindow? RobloxWindow { get; private set; }

    public PixelSize? ObservedClientSize { get; private set; }

    public PixelSize TargetSize => new(_settings.TargetWidth, _settings.TargetHeight);

    public int FrameCount => _settings.FrameCount;

    public double DurationSeconds => _settings.DurationSeconds;

    public DatasetCaptureMode CaptureMode => _settings.CaptureMode;

    public string DatasetRoot => _settings.DatasetRoot;

    public DatasetLocation? ActiveDataset { get; private set; }

    public DatasetLocation? RecentDataset { get; private set; }

    public bool IsManualCaptureActive => _manualCaptureDataset is not null;

    public bool WindowIsReady => RobloxWindow is not null && ObservedClientSize == TargetSize;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _deepDebug.RecordEvent("workspace", "initialize_started");
        _settings = await _settingsStore.LoadAsync(cancellationToken);
        await RefreshWindowAsync(cancellationToken);
        IReadOnlyList<DatasetLocation> datasets = await _datasets.DiscoverAsync(DatasetRoot, cancellationToken);
        RecentDataset = datasets.FirstOrDefault();
        _deepDebug.RecordEvent("workspace", "initialize_completed", new { TargetSize, DatasetRoot, RecentDataset = RecentDataset?.DirectoryPath });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshWindowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RobloxWindow = _windows.FindBest();
        ObservedClientSize = RobloxWindow is { } window ? _windows.GetClientBounds(window).Size : null;
        _deepDebug.RecordEvent("window", "refreshed", new { Found = RobloxWindow is not null, RobloxWindow?.Title, RobloxWindow?.ProcessId, ObservedClientSize });
        Changed?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public async Task<ResizeResult> ApplyTargetSizeAsync(CancellationToken cancellationToken = default)
        => await ApplyClientSizeAsync(TargetSize, cancellationToken);

    public async Task<ResizeResult> ApplyClientSizeAsync(
        PixelSize target,
        CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        }
        try
        {
            RobloxWindow window = RobloxWindow ?? _windows.FindBest()
                ?? throw new InvalidOperationException("Start Roblox in windowed mode, then try again.");
            RobloxWindow = window;
            ResizeResult result = await _windows.ResizeClientAsync(window, target, cancellationToken);
            ObservedClientSize = _windows.GetClientBounds(window).Size;
            _deepDebug.RecordEvent("window", "client_resized", new
            {
                Requested = target,
                ObservedClientSize,
                Result = result,
            });
            Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<CapturedPng> CaptureLiveFrameAsync(
        PixelSize requiredSize,
        CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        }
        try
        {
            RobloxWindow window = RobloxWindow ?? _windows.FindBest()
                ?? throw new InvalidOperationException("Start Roblox in windowed mode before running Debug OCR.");
            RobloxWindow = window;
            if (_windows.GetClientBounds(window).Size != requiredSize)
            {
                await _windows.ResizeClientAsync(window, requiredSize, cancellationToken);
            }

            CapturedPng image = await Task.Run(() => _capture.Capture(window), cancellationToken);
            ObservedClientSize = _windows.GetClientBounds(window).Size;
            if (image.Size != requiredSize || ObservedClientSize != requiredSize)
            {
                throw new InvalidOperationException(
                    $"Debug capture requires {requiredSize}; captured {image.Size} and observed {ObservedClientSize}.");
            }
            _deepDebug.RecordPng(image.Bytes, "live-client", new
            {
                image.Size,
                RequiredSize = requiredSize,
                ObservedClientSize,
            });
            Changed?.Invoke(this, EventArgs.Empty);
            return image;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<CapturedGrayRegion>> CaptureDetectorRegionsAsync(
        PixelSize requiredSize,
        IReadOnlyList<PixelRect> regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0) return [];
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        }
        try
        {
            RobloxWindow window = RobloxWindow ?? _windows.FindBest()
                ?? throw new InvalidOperationException("Start Roblox in windowed mode before testing image matching.");
            RobloxWindow = window;
            if (_windows.GetClientBounds(window).Size != requiredSize)
            {
                await _windows.ResizeClientAsync(window, requiredSize, cancellationToken);
            }
            IReadOnlyList<CapturedGrayRegion> captures = await Task.Run(
                () => _capture.CaptureDetectorRegions(window, regions),
                cancellationToken);
            ObservedClientSize = _windows.GetClientBounds(window).Size;
            if (ObservedClientSize != requiredSize)
            {
                throw new InvalidOperationException(
                    $"Image comparison requires {requiredSize}; observed {ObservedClientSize}.");
            }
            for (int index = 0; index < captures.Count; index++)
            {
                CapturedGrayRegion capture = captures[index];
                _deepDebug.RecordGrayImage(capture.Image, $"detector-region-{index + 1}", new
                {
                    capture.Region,
                    RequiredSize = requiredSize,
                });
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return captures;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<CapturedRgbRegion>> CaptureRgbRegionsAsync(
        PixelSize requiredSize,
        IReadOnlyList<PixelRect> regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0) return [];
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        try
        {
            RobloxWindow window = RobloxWindow ?? _windows.FindBest()
                ?? throw new InvalidOperationException("Start Roblox in windowed mode before capturing unit controls.");
            RobloxWindow = window;
            if (_windows.GetClientBounds(window).Size != requiredSize)
                await _windows.ResizeClientAsync(window, requiredSize, cancellationToken);
            IReadOnlyList<CapturedRgbRegion> captures = await Task.Run(
                () => _capture.CaptureRgbRegions(window, regions), cancellationToken);
            ObservedClientSize = _windows.GetClientBounds(window).Size;
            if (ObservedClientSize != requiredSize)
                throw new InvalidOperationException($"Unit control capture requires {requiredSize}; observed {ObservedClientSize}.");
            foreach (CapturedRgbRegion capture in captures)
                _deepDebug.RecordPng(PngEncoder.Encode(capture.Image), "unit-control-region", new { capture.Region });
            Changed?.Invoke(this, EventArgs.Empty);
            return captures;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task FocusRobloxAsync(
        PixelSize requiredSize,
        CancellationToken cancellationToken = default) =>
        _input.FocusAsync(requiredSize, cancellationToken);

    public Task ClickRobloxAsync(
        PixelSize requiredSize,
        PixelPoint point,
        CancellationToken cancellationToken = default) =>
        _input.ClickAsync(requiredSize, point, cancellationToken);

    public Task HoverRobloxAsync(
        PixelSize requiredSize,
        PixelPoint point,
        CancellationToken cancellationToken = default) =>
        _input.HoverAsync(requiredSize, point, cancellationToken);

    public Task ScrollRobloxAsync(
        PixelSize requiredSize,
        PixelPoint point,
        int wheelDelta,
        TimeSpan duration,
        CancellationToken cancellationToken = default) =>
        _input.ScrollAsync(requiredSize, point, wheelDelta, duration, cancellationToken);

    public Task DragRobloxAsync(
        PixelSize requiredSize,
        PixelPoint start,
        PixelPoint end,
        TimeSpan duration,
        CancellationToken cancellationToken = default) =>
        _input.DragAsync(requiredSize, start, end, duration, cancellationToken);

    public Task RunKeySequenceAsync(
        PixelSize requiredSize,
        AutomationKeySequence sequence,
        CancellationToken cancellationToken = default) =>
        _input.RunKeysAsync(requiredSize, sequence, cancellationToken);

    public Task RunTextInputAsync(PixelSize requiredSize, string value, CancellationToken cancellationToken = default) => _input.RunTextAsync(requiredSize, value, cancellationToken);
    public Task RunQuickPlacementBatchAsync(
        PixelSize requiredSize,
        int quickPlacementVirtualKey,
        int cancelPlacementVirtualKey,
        IReadOnlyList<QuickPlacementPoint> placements,
        CancellationToken cancellationToken = default) =>
        _input.RunQuickPlacementAsync(
            requiredSize, quickPlacementVirtualKey, cancelPlacementVirtualKey, placements, cancellationToken);

    public Task AlignCameraAsync(
        PixelSize requiredSize,
        int shiftLockVirtualKey = KeyboardKey.LeftShift,
        CancellationToken cancellationToken = default) =>
        _input.AlignCameraAsync(requiredSize, shiftLockVirtualKey, cancellationToken);

    public async Task UpdateSettingsAsync(
        int targetWidth,
        int targetHeight,
        int frameCount,
        double durationSeconds,
        DatasetCaptureMode captureMode,
        string datasetRoot,
        CancellationToken cancellationToken = default)
    {
        if (IsManualCaptureActive) throw new InvalidOperationException("Finish the manual capture before changing settings.");
        PixelSize target = PixelSize.Create(targetWidth, targetHeight);
        if (captureMode is not (DatasetCaptureMode.Timed or DatasetCaptureMode.Manual))
        {
            throw new ArgumentOutOfRangeException(nameof(captureMode));
        }
        CapturePlan plan = new()
        {
            TargetSize = target,
            FrameCount = frameCount,
            Duration = TimeSpan.FromSeconds(durationSeconds),
        };
        plan.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        _settings = new AppSettings
        {
            TargetWidth = target.Width,
            TargetHeight = target.Height,
            FrameCount = plan.FrameCount,
            DurationSeconds = plan.Duration.TotalSeconds,
            CaptureMode = captureMode,
            DatasetRoot = Path.GetFullPath(datasetRoot),
        };
        await _settingsStore.SaveAsync(_settings, cancellationToken);
        if (RobloxWindow is { } window) ObservedClientSize = _windows.GetClientBounds(window).Size;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<DatasetLocation> CaptureDatasetAsync(
        IProgress<CaptureProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsManualCaptureActive)
        {
            throw new InvalidOperationException("Finish the manual capture before starting a timed dataset.");
        }
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        }
        try
        {
            RobloxWindow window = RobloxWindow ?? _windows.FindBest()
                ?? throw new InvalidOperationException("Start Roblox in windowed mode before capturing.");
            RobloxWindow = window;
            if (_windows.GetClientBounds(window).Size != TargetSize)
            {
                progress?.Report(new CaptureProgress(0, FrameCount, $"Sizing Roblox to {TargetSize}"));
                await _windows.ResizeClientAsync(window, TargetSize, cancellationToken);
            }

            CapturePlan plan = new()
            {
                TargetSize = TargetSize,
                FrameCount = FrameCount,
                Duration = TimeSpan.FromSeconds(DurationSeconds),
            };
            DatasetLocation location = await _datasets.CreateDraftAsync(
                DatasetRoot,
                plan,
                window.Title,
                window.ProcessId,
                DateTimeOffset.UtcNow,
                cancellationToken);
            ActiveDataset = location;
            RecentDataset = location;
            Changed?.Invoke(this, EventArgs.Empty);

            IReadOnlyList<TimeSpan> schedule = CaptureSchedule.Create(plan);
            Stopwatch clock = Stopwatch.StartNew();
            for (int index = 0; index < schedule.Count; index++)
            {
                TimeSpan delay = schedule[index] - clock.Elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                PixelSize observed = _windows.GetClientBounds(window).Size;
                if (observed != TargetSize)
                {
                    throw new InvalidOperationException(
                        $"Roblox changed to {observed} during capture; the draft was stopped before mixing resolutions.");
                }

                progress?.Report(new CaptureProgress(index, schedule.Count, $"Capturing frame {index + 1} of {schedule.Count}"));
                CapturedPng image = await Task.Run(() => _capture.Capture(window), cancellationToken);
                _deepDebug.RecordPng(image.Bytes, "dataset-frame", new
                {
                    Index = index + 1,
                    Total = schedule.Count,
                    Dataset = location.DirectoryPath,
                    image.Size,
                });
                await _datasets.AddFrameAsync(
                    location,
                    image.Bytes,
                    image.Size.Width,
                    image.Size.Height,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                progress?.Report(new CaptureProgress(index + 1, schedule.Count, $"Saved frame {index + 1} of {schedule.Count}"));
            }

            ObservedClientSize = _windows.GetClientBounds(window).Size;
            Changed?.Invoke(this, EventArgs.Empty);
            return location;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DatasetLocation> StartManualCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        }
        try
        {
            if (_manualCaptureDataset is not null)
            {
                throw new InvalidOperationException("A manual capture is already active.");
            }
            RobloxWindow window = RobloxWindow ?? _windows.FindBest()
                ?? throw new InvalidOperationException("Start Roblox in windowed mode before capturing.");
            RobloxWindow = window;
            if (_windows.GetClientBounds(window).Size != TargetSize)
            {
                await _windows.ResizeClientAsync(window, TargetSize, cancellationToken);
            }

            DatasetLocation location = await _datasets.CreateManualDraftAsync(
                DatasetRoot,
                TargetSize,
                window.Title,
                window.ProcessId,
                DateTimeOffset.UtcNow,
                cancellationToken);
            _manualCaptureDataset = location;
            ActiveDataset = location;
            RecentDataset = location;
            ObservedClientSize = _windows.GetClientBounds(window).Size;
            Changed?.Invoke(this, EventArgs.Empty);
            return location;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DatasetFrame> CaptureManualFrameAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        }
        try
        {
            DatasetLocation location = _manualCaptureDataset
                ?? throw new InvalidOperationException("Start a manual capture first.");
            RobloxWindow window = RobloxWindow
                ?? throw new InvalidOperationException("The manual capture's Roblox window is no longer available.");
            if (window.ProcessId != location.Manifest.SourceProcessId)
            {
                throw new InvalidOperationException("The manual capture's Roblox process changed.");
            }

            PixelSize observed = _windows.GetClientBounds(window).Size;
            ObservedClientSize = observed;
            if (observed != TargetSize)
            {
                Changed?.Invoke(this, EventArgs.Empty);
                throw new InvalidOperationException($"Roblox is {observed}; manual capture requires {TargetSize}.");
            }

            CapturedPng image = await Task.Run(() => _capture.Capture(window), cancellationToken);
            _deepDebug.RecordPng(image.Bytes, "manual-dataset-frame", new
            {
                Dataset = location.DirectoryPath,
                image.Size,
            });
            DatasetFrame frame = await _datasets.AddFrameAsync(
                location,
                image.Bytes,
                image.Size.Width,
                image.Size.Height,
                DateTimeOffset.UtcNow,
                cancellationToken);
            ObservedClientSize = _windows.GetClientBounds(window).Size;
            Changed?.Invoke(this, EventArgs.Empty);
            return frame;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public DatasetLocation? EndManualCapture()
    {
        DatasetLocation? completed = _manualCaptureDataset;
        _manualCaptureDataset = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return completed;
    }

    public async Task<DatasetLocation> OpenDatasetAsync(string directory, CancellationToken cancellationToken = default)
    {
        DatasetLocation opened = await _datasets.LoadAsync(directory, cancellationToken);
        _manualCaptureDataset = null;
        ActiveDataset = opened;
        RecentDataset = ActiveDataset;
        Changed?.Invoke(this, EventArgs.Empty);
        return ActiveDataset;
    }

    public Task SaveActiveDatasetAsync(CancellationToken cancellationToken = default) =>
        ActiveDataset is null
            ? Task.FromException(new InvalidOperationException("No dataset is open."))
            : _datasets.SaveAsync(ActiveDataset, cancellationToken);

    public async Task<DatasetLocation> FinalizeActiveDatasetAsync(
        string name,
        string notes,
        CancellationToken cancellationToken = default)
    {
        DatasetLocation active = ActiveDataset ?? throw new InvalidOperationException("No dataset is open.");
        DatasetLocation finalized = await _datasets.FinalizeAsync(active, name, notes, cancellationToken);
        _manualCaptureDataset = null;
        ActiveDataset = finalized;
        RecentDataset = ActiveDataset;
        Changed?.Invoke(this, EventArgs.Empty);
        return ActiveDataset;
    }

    public Task<IReadOnlyList<DatasetLocation>> DiscoverDatasetsAsync(CancellationToken cancellationToken = default) =>
        _datasets.DiscoverAsync(DatasetRoot, cancellationToken);

    public void Dispose()
    {
        _capture.Dispose();
        _operationGate.Dispose();
    }
}
