using System.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Capture;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Windows;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Workspace;

public sealed class WorkspaceController : IDisposable
{
    private readonly AppSettingsStore _settingsStore = new();
    private readonly DatasetStore _datasets = new();
    private readonly RobloxWindowService _windows = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly RobloxCaptureService _capture;
    private AppSettings _settings = new();

    public WorkspaceController()
    {
        _capture = new RobloxCaptureService(_windows);
    }

    public event EventHandler? Changed;

    public RobloxWindow? RobloxWindow { get; private set; }

    public PixelSize? ObservedClientSize { get; private set; }

    public PixelSize TargetSize => new(_settings.TargetWidth, _settings.TargetHeight);

    public int FrameCount => _settings.FrameCount;

    public double DurationSeconds => _settings.DurationSeconds;

    public string DatasetRoot => _settings.DatasetRoot;

    public DatasetLocation? ActiveDataset { get; private set; }

    public DatasetLocation? RecentDataset { get; private set; }

    public bool WindowIsReady => RobloxWindow is not null && ObservedClientSize == TargetSize;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken);
        await RefreshWindowAsync(cancellationToken);
        IReadOnlyList<DatasetLocation> datasets = await _datasets.DiscoverAsync(DatasetRoot, cancellationToken);
        RecentDataset = datasets.FirstOrDefault();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshWindowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RobloxWindow = _windows.FindBest();
        ObservedClientSize = RobloxWindow is { } window ? _windows.GetClientBounds(window).Size : null;
        Changed?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public async Task<ResizeResult> ApplyTargetSizeAsync(CancellationToken cancellationToken = default)
    {
        RobloxWindow window = RobloxWindow ?? _windows.FindBest()
            ?? throw new InvalidOperationException("Start Roblox in windowed mode, then try again.");
        RobloxWindow = window;
        ResizeResult result = await _windows.ResizeClientAsync(window, TargetSize, cancellationToken);
        ObservedClientSize = _windows.GetClientBounds(window).Size;
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public async Task UpdateSettingsAsync(
        int targetWidth,
        int targetHeight,
        int frameCount,
        double durationSeconds,
        string datasetRoot,
        CancellationToken cancellationToken = default)
    {
        PixelSize target = PixelSize.Create(targetWidth, targetHeight);
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

    public async Task<DatasetLocation> OpenDatasetAsync(string directory, CancellationToken cancellationToken = default)
    {
        ActiveDataset = await _datasets.LoadAsync(directory, cancellationToken);
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
        ActiveDataset = await _datasets.FinalizeAsync(active, name, notes, cancellationToken);
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
