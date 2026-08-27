using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal sealed record TeamSwapScrollCalibrationResult(
    TeamSwapCalibration Calibration,
    DebugOcrSnapshot TopSnapshot,
    PixelRect SearchRegion,
    TeamScrollbarEndpoints Endpoints)
{
    public PixelRect SearchRegionFor(PixelRect currentTitle) =>
        ClampToClient(Translate(SearchRegion, currentTitle), Calibration.ClientSize);

    public TeamScrollbarEndpoints EndpointsFor(PixelRect currentTitle) => new(
        Translate(Endpoints.TopBounds, currentTitle),
        Translate(Endpoints.BottomBounds, currentTitle));

    private PixelRect Translate(PixelRect bounds, PixelRect currentTitle) => bounds with
    {
        X = checked(bounds.X + currentTitle.X - Calibration.TitleBounds.X),
        Y = checked(bounds.Y + currentTitle.Y - Calibration.TitleBounds.Y),
    };

    internal static PixelRect ClampToClient(PixelRect bounds, PixelSize clientSize)
    {
        int left = Math.Clamp(bounds.X, 0, clientSize.Width - 1);
        int top = Math.Clamp(bounds.Y, 0, clientSize.Height - 1);
        int right = Math.Clamp(bounds.Right, left + 1, clientSize.Width);
        int bottom = Math.Clamp(bounds.Bottom, top + 1, clientSize.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}

internal sealed class TeamSwapScrollCalibrator(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private const int EndpointWheelUnits = 10000;
    private const int ProbeWheelUnits = 600;
    private const int ScrollMilliseconds = 280;
    private const int SettleMilliseconds = 90;
    private const int CaptureSampleCount = 6;
    private const int CaptureSampleIntervalMilliseconds = 45;
    private const int MaximumStateObservations = 3;
    private static readonly TimeSpan StateRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<TeamSwapScrollCalibrationResult?> CalibrateAsync(
        TeamSwapLayout initialLayout,
        string device,
        List<string> events,
        CancellationToken cancellationToken)
    {
        PixelRect searchRegion = TeamScrollbarDetector.CreateSearchRegion(
            initialLayout,
            DebugWorkflowCatalog.ClientSize);
        events.Add(
            $"SLOW SCROLL CALIBRATION ROI [{searchRegion.X},{searchRegion.Y}," +
            $"{searchRegion.Width},{searchRegion.Height}]");

        await ResetTopAsync(initialLayout.ScrollAnchor.Bounds.Center, cancellationToken);
        RgbImage[] topFrames = await CaptureSequenceAsync(searchRegion, cancellationToken);
        DebugOcrSnapshot topSnapshot = await RunTeamStateAsync(device, cancellationToken);
        TeamSwapLayout? topLayout = CreateLayout(topSnapshot);
        if (topLayout is null)
        {
            events.Add("SCROLL CALIBRATION FAILED | TOP TEAM LAYOUT NOT VERIFIED");
            return null;
        }

        bool needsTopReset = false;
        try
        {
            await ScrollEndpointAsync(topLayout.ScrollAnchor.Bounds.Center, downward: true, cancellationToken);
            needsTopReset = true;
            RgbImage[] bottomFrames = await CaptureSequenceAsync(searchRegion, cancellationToken);
            DebugOcrSnapshot bottomSnapshot = await RunTeamStateAsync(device, cancellationToken);
            TeamSwapLayout? bottomLayout = CreateLayout(bottomSnapshot);
            if (bottomLayout is null)
            {
                events.Add("SCROLL CALIBRATION FAILED | BOTTOM TEAM LAYOUT NOT VERIFIED");
                return null;
            }

            TeamScrollbarEndpoints? endpoints = TeamScrollbarDetector.TryCalibrate(
                topFrames,
                bottomFrames,
                searchRegion,
                out TeamScrollbarCalibrationDiagnostics scrollbarDiagnostics);
            events.Add(
                $"SCROLLBAR CANDIDATES | TOP {FormatCandidates(scrollbarDiagnostics.TopCandidates)} | " +
                $"BOTTOM {FormatCandidates(scrollbarDiagnostics.BottomCandidates)}");
            if (endpoints is null)
            {
                events.Add("SCROLL CALIBRATION FAILED | MOVING THUMB ENDPOINT PAIR NOT FOUND");
                return null;
            }
            TeamSwapCalibration? calibration = TeamSwapCalibration.TryCreate(
                DebugWorkflowCatalog.ClientSize,
                topLayout,
                bottomLayout,
                endpoints.TopBounds,
                endpoints.BottomBounds);
            if (calibration is null)
            {
                events.Add("SCROLL CALIBRATION FAILED | TEAM ROW OR THUMB GEOMETRY REJECTED");
                return null;
            }
            events.Add(
                $"SCROLLBAR TOP [{endpoints.TopBounds.X},{endpoints.TopBounds.Y}," +
                $"{endpoints.TopBounds.Width},{endpoints.TopBounds.Height}] BOTTOM " +
                $"[{endpoints.BottomBounds.X},{endpoints.BottomBounds.Y}," +
                $"{endpoints.BottomBounds.Width},{endpoints.BottomBounds.Height}] " +
                $"PITCH {calibration.RowPitch}");

            await ResetTopAsync(bottomLayout.ScrollAnchor.Bounds.Center, cancellationToken);
            needsTopReset = false;
            await ScrollAsync(topLayout.ScrollAnchor.Bounds.Center, -ProbeWheelUnits, cancellationToken);
            needsTopReset = true;
            TeamScrollbarObservation? probe = await ObserveAsync(
                searchRegion,
                endpoints,
                cancellationToken);
            int? middleUnits = probe is null
                ? null
                : TeamSwapCalibration.EstimateMiddleWheelUnits(
                    ProbeWheelUnits,
                    probe.NormalizedPosition);
            if (middleUnits is null)
            {
                events.Add(probe is null
                    ? "SCROLL CALIBRATION FAILED | MIDDLE THUMB NOT OBSERVED"
                    : $"SCROLL CALIBRATION FAILED | MIDDLE WHEEL ESTIMATE REJECTED AT {probe.NormalizedPosition:P2}");
                return null;
            }
            calibration = calibration with { MiddleWheelUnits = middleUnits.Value };
            events.Add(
                $"WHEEL PROBE {ProbeWheelUnits} -> {probe!.NormalizedPosition:P2}; " +
                $"MIDDLE {middleUnits.Value} UNITS");

            await ResetTopAsync(topLayout.ScrollAnchor.Bounds.Center, cancellationToken);
            needsTopReset = false;
            DebugOcrSnapshot restoredTop = await RunTeamStateAsync(device, cancellationToken);
            if (CreateLayout(restoredTop) is null)
            {
                events.Add("SCROLL CALIBRATION FAILED | TOP TEAM LAYOUT NOT RESTORED");
                return null;
            }
            events.Add("SCROLLBAR RESTORED TOP; WHEEL CALIBRATION STORED");
            return new TeamSwapScrollCalibrationResult(
                calibration,
                restoredTop,
                searchRegion,
                endpoints);
        }
        finally
        {
            if (needsTopReset)
            {
                try
                {
                    await ResetTopAsync(
                        initialLayout.ScrollAnchor.Bounds.Center,
                        CancellationToken.None);
                    events.Add("SCROLL CALIBRATION CLEANUP | RESTORED TOP AFTER FAILURE");
                }
                catch (InvalidOperationException)
                {
                    events.Add("SCROLL CALIBRATION CLEANUP | TOP RESTORE BLOCKED BY CLIENT CHANGE");
                    // Input cleanup is complete; a closed or resized client owns the failure.
                }
            }
        }
    }

    public async Task<TeamScrollbarObservation?> ObserveAsync(
        TeamSwapScrollCalibrationResult calibration,
        PixelRect currentTitle,
        CancellationToken cancellationToken)
    {
        PixelRect searchRegion = calibration.SearchRegionFor(currentTitle);
        TeamScrollbarEndpoints endpoints = calibration.EndpointsFor(currentTitle);
        return await ObserveAsync(searchRegion, endpoints, cancellationToken);
    }

    private async Task<TeamScrollbarObservation?> ObserveAsync(
        PixelRect searchRegion,
        TeamScrollbarEndpoints endpoints,
        CancellationToken cancellationToken)
    {
        RgbImage[] frames = await CaptureSequenceAsync(searchRegion, cancellationToken);
        return TeamScrollbarDetector.TryObserve(frames, searchRegion, endpoints);
    }

    private async Task<RgbImage[]> CaptureSequenceAsync(
        PixelRect region,
        CancellationToken cancellationToken)
    {
        await Task.Delay(SettleMilliseconds, cancellationToken);
        RgbImage[] frames = new RgbImage[CaptureSampleCount];
        for (int index = 0; index < frames.Length; index++)
        {
            if (index > 0)
            {
                await Task.Delay(CaptureSampleIntervalMilliseconds, cancellationToken);
            }

            frames[index] = await CaptureAsync(region, cancellationToken);
        }

        return frames;
    }

    private async Task<RgbImage> CaptureAsync(
        PixelRect region,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LilacMacro.Windows.Capture.CapturedRgbRegion> captures =
            await workspace.CaptureRgbRegionsAsync(
                DebugWorkflowCatalog.ClientSize,
                [region],
                cancellationToken);
        return captures.Single().Image;
    }

    private Task ResetTopAsync(PixelPoint anchor, CancellationToken cancellationToken) =>
        ScrollAsync(anchor, EndpointWheelUnits, cancellationToken);

    private Task ScrollEndpointAsync(
        PixelPoint anchor,
        bool downward,
        CancellationToken cancellationToken) =>
        ScrollAsync(anchor, downward ? -EndpointWheelUnits : EndpointWheelUnits, cancellationToken);

    private Task ScrollAsync(
        PixelPoint anchor,
        int units,
        CancellationToken cancellationToken) => workspace.ScrollRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            anchor,
            units,
            TimeSpan.FromMilliseconds(ScrollMilliseconds),
            cancellationToken);

    private Task<DebugOcrSnapshot> RunTeamStateAsync(
        string device,
        CancellationToken cancellationToken) => _states.WaitForMatchAsync(
        DebugWorkflowCatalog.TeamSwap,
        device,
        MaximumStateObservations,
        StateRetryDelay,
        cancellationToken);

    private static TeamSwapLayout? CreateLayout(DebugOcrSnapshot snapshot) =>
        TeamSwapLayout.TryCreate(snapshot.Regions, DebugWorkflowCatalog.ClientSize);

    private static string FormatCandidates(IReadOnlyList<PixelRect> candidates) =>
        candidates.Count == 0
            ? "NONE"
            : string.Join(",", candidates.Select(bounds =>
                $"[{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}]"));
}
