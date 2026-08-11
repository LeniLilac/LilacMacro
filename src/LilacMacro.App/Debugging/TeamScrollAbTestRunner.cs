using System.Text.Json;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Debugging;

internal enum TeamScrollTestMethod
{
    Drag,
    Scroll,
}

internal sealed record TeamScrollTrialResult(
    int Trial,
    TeamScrollTestMethod Method,
    int? ScrollUnits,
    double? Position,
    PixelRect? ThumbBounds,
    string BeforeImage,
    string AfterImage,
    string Status);

internal sealed record TeamScrollTestResult(
    TeamScrollTestMethod Method,
    string OutputDirectory,
    IReadOnlyList<TeamScrollTrialResult> Trials,
    string Status);

internal sealed record TeamScrollTestProgress(
    int Completed,
    int Total,
    TeamScrollTrialResult? Trial,
    string Detail);

internal sealed class TeamScrollAbTestRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private const int EndpointWheelUnits = 10000;
    private const int EndpointScrollMilliseconds = 280;
    private const int SettleMilliseconds = 90;
    private const int DragMilliseconds = 180;
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<TeamScrollTestResult> RunAsync(
        TeamScrollTestMethod method,
        int trialCount,
        int downwardWheelUnits,
        int wheelIncrement,
        string device,
        IProgress<TeamScrollTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<int> scrollSchedule = method == TeamScrollTestMethod.Scroll
            ? ScrollTrialSchedule.Create(downwardWheelUnits, wheelIncrement, trialCount)
            : ScrollTrialSchedule.Create(downwardWheelUnits, 0, trialCount);

        string outputDirectory = CreateOutputDirectory(method);
        CalibrationContext calibration = await CalibrateAsync(device, cancellationToken);
        List<TeamScrollTrialResult> trials = [];
        try
        {
            for (int trial = 1; trial <= trialCount; trial++)
            {
                int? appliedScrollUnits = method == TeamScrollTestMethod.Scroll
                    ? scrollSchedule[trial - 1]
                    : null;
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new TeamScrollTestProgress(
                    trial - 1, trialCount, null, $"TRIAL {trial}/{trialCount} RESET"));
                await ResetTopAsync(calibration.ScrollAnchor, cancellationToken);

                string prefix = $"{method.ToString().ToLowerInvariant()}-{trial:000}";
                string beforePath = Path.Combine(outputDirectory, $"{prefix}-before.png");
                string afterPath = Path.Combine(outputDirectory, $"{prefix}-after.png");
                await SaveFrameAsync(beforePath, cancellationToken);

                if (method == TeamScrollTestMethod.Drag)
                {
                    await workspace.DragRobloxAsync(
                        DebugWorkflowCatalog.ClientSize,
                        calibration.DragStart,
                        calibration.DragEnd,
                        TimeSpan.FromMilliseconds(DragMilliseconds),
                        cancellationToken);
                }
                else
                {
                    await workspace.ScrollRobloxAsync(
                        DebugWorkflowCatalog.ClientSize,
                        calibration.ScrollAnchor,
                        -appliedScrollUnits!.Value,
                        TimeSpan.FromMilliseconds(EndpointScrollMilliseconds),
                        cancellationToken);
                }

                await Task.Delay(SettleMilliseconds, cancellationToken);
                await SaveFrameAsync(afterPath, cancellationToken);
                TeamScrollbarObservation? observation = await ObserveAsync(calibration, cancellationToken);
                TeamScrollTrialResult result = new(
                    trial,
                    method,
                    appliedScrollUnits,
                    observation?.NormalizedPosition,
                    observation?.Bounds,
                    beforePath,
                    afterPath,
                    observation is null ? "THUMB NOT FOUND" : "MEASURED");
                trials.Add(result);
                progress?.Report(new TeamScrollTestProgress(
                    trial, trialCount, result,
                    observation is null
                        ? $"TRIAL {trial}/{trialCount} THUMB NOT FOUND"
                        : appliedScrollUnits is null
                            ? $"TRIAL {trial}/{trialCount} POSITION {observation.NormalizedPosition:P1}"
                            : $"TRIAL {trial}/{trialCount} UNITS {appliedScrollUnits} POSITION {observation.NormalizedPosition:P1}"));
            }
        }
        finally
        {
            await ResetTopAsync(calibration.ScrollAnchor, CancellationToken.None);
            await SaveFrameAsync(Path.Combine(outputDirectory, "final-reset.png"), CancellationToken.None);
        }

        TeamScrollTestResult test = new(
            method,
            outputDirectory,
            trials,
            trials.All(trial => trial.Position is not null) ? "COMPLETE" : "COMPLETE WITH MISSES");
        await WriteResultsAsync(
            test,
            downwardWheelUnits,
            wheelIncrement,
            cancellationToken);
        return test;
    }

    private async Task<CalibrationContext> CalibrateAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot initial = await _states.RunAsync(
            DebugWorkflowCatalog.TeamSwap, device, cancellationToken);
        TeamSwapLayout layout = TeamSwapLayout.TryCreate(initial.Regions, DebugWorkflowCatalog.ClientSize)
            ?? throw new InvalidOperationException("Unit Teams and at least two Save/Load rows are required.");
        PixelRect search = TeamScrollbarDetector.CreateSearchRegion(layout, DebugWorkflowCatalog.ClientSize);

        await ResetTopAsync(layout.ScrollAnchor.Bounds.Center, cancellationToken);
        RgbImage[] topFrames = await CapturePairAsync(search, cancellationToken);
        DebugOcrSnapshot topSnapshot = await _states.RunAsync(
            DebugWorkflowCatalog.TeamSwap, device, cancellationToken);
        TeamSwapLayout top = TeamSwapLayout.TryCreate(topSnapshot.Regions, DebugWorkflowCatalog.ClientSize)
            ?? throw new InvalidOperationException("Could not resolve the top team rows.");

        bool movedDown = false;
        try
        {
            await ScrollEndpointAsync(top.ScrollAnchor.Bounds.Center, downward: true, cancellationToken);
            movedDown = true;
            RgbImage[] bottomFrames = await CapturePairAsync(search, cancellationToken);
            DebugOcrSnapshot bottomSnapshot = await _states.RunAsync(
                DebugWorkflowCatalog.TeamSwap, device, cancellationToken);
            TeamSwapLayout bottom = TeamSwapLayout.TryCreate(bottomSnapshot.Regions, DebugWorkflowCatalog.ClientSize)
                ?? throw new InvalidOperationException("Could not resolve the bottom team rows.");

            TeamScrollbarEndpoints endpoints = TeamScrollbarDetector.TryCalibrate(
                topFrames, bottomFrames, search)
                ?? throw new InvalidOperationException("Could not calibrate the moving gray scrollbar thumb.");
            TeamSwapCalibration swap = TeamSwapCalibration.TryCreate(
                DebugWorkflowCatalog.ClientSize,
                top,
                bottom,
                endpoints.TopBounds,
                endpoints.BottomBounds)
                ?? throw new InvalidOperationException("The scrollbar endpoints or visible team rows were inconsistent.");
            TeamSwapResolvedTarget middle = swap.Resolve(4, top.TitleBounds)
                ?? throw new InvalidOperationException("Could not derive the middle scrollbar target.");
            if (middle.DragStart is null || middle.DragEnd is null)
                throw new InvalidOperationException("Middle drag coordinates are unavailable.");

            await ResetTopAsync(middle.ScrollAnchor, cancellationToken);
            movedDown = false;
            return new CalibrationContext(
                search,
                endpoints,
                middle.ScrollAnchor,
                middle.DragStart.Value,
                middle.DragEnd.Value);
        }
        finally
        {
            if (movedDown)
            {
                try
                {
                    await ResetTopAsync(top.ScrollAnchor.Bounds.Center, CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    // Input cleanup has completed; a closed or resized client owns this failure.
                }
            }
        }
    }

    private async Task<TeamScrollbarObservation?> ObserveAsync(
        CalibrationContext calibration,
        CancellationToken cancellationToken)
    {
        RgbImage[] frames = await CapturePairAsync(calibration.SearchRegion, cancellationToken);
        return TeamScrollbarDetector.TryObserve(frames, calibration.SearchRegion, calibration.Endpoints);
    }

    private async Task<RgbImage[]> CapturePairAsync(
        PixelRect region,
        CancellationToken cancellationToken)
    {
        RgbImage first = await CaptureRegionAsync(region, cancellationToken);
        await Task.Delay(40, cancellationToken);
        RgbImage second = await CaptureRegionAsync(region, cancellationToken);
        return [first, second];
    }

    private async Task<RgbImage> CaptureRegionAsync(
        PixelRect region,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize, [region], cancellationToken);
        return captures.Single().Image;
    }

    private async Task SaveFrameAsync(string path, CancellationToken cancellationToken)
    {
        CapturedPng frame = await workspace.CaptureLiveFrameAsync(
            DebugWorkflowCatalog.ClientSize, cancellationToken);
        await File.WriteAllBytesAsync(path, frame.Bytes, cancellationToken);
    }

    private Task ResetTopAsync(PixelPoint anchor, CancellationToken cancellationToken) =>
        ScrollEndpointAsync(anchor, downward: false, cancellationToken);

    private async Task ScrollEndpointAsync(
        PixelPoint anchor,
        bool downward,
        CancellationToken cancellationToken)
    {
        await workspace.ScrollRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            anchor,
            downward ? -EndpointWheelUnits : EndpointWheelUnits,
            TimeSpan.FromMilliseconds(EndpointScrollMilliseconds),
            cancellationToken);
        await Task.Delay(SettleMilliseconds, cancellationToken);
    }

    private static string CreateOutputDirectory(TeamScrollTestMethod method)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "diagnostics");
        string directory = Path.Combine(
            root,
            $"team-scroll-{method.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WriteResultsAsync(
        TeamScrollTestResult result,
        int startingWheelUnits,
        int wheelIncrement,
        CancellationToken cancellationToken)
    {
        double[] measured = result.Trials
            .Where(trial => trial.Position is not null)
            .Select(trial => trial.Position!.Value)
            .ToArray();
        double mean = measured.Length == 0 ? 0 : measured.Average();
        double standardDeviation = measured.Length == 0
            ? 0
            : Math.Sqrt(measured.Average(value => Math.Pow(value - mean, 2)));
        object document = new
        {
            result.Method,
            StartingWheelUnits = startingWheelUnits,
            WheelIncrement = result.Method == TeamScrollTestMethod.Scroll
                ? wheelIncrement
                : (int?)null,
            result.Status,
            MeanPosition = mean,
            StandardDeviation = standardDeviation,
            MeasuredTrials = measured.Length,
            TotalTrials = result.Trials.Count,
            result.Trials,
        };
        await File.WriteAllTextAsync(
            Path.Combine(result.OutputDirectory, "results.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private sealed record CalibrationContext(
        PixelRect SearchRegion,
        TeamScrollbarEndpoints Endpoints,
        PixelPoint ScrollAnchor,
        PixelPoint DragStart,
        PixelPoint DragEnd);
}
