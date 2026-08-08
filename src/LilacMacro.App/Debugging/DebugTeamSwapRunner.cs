using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;
using static LilacMacro.App.Debugging.DebugReportFactory;

namespace LilacMacro.App.Debugging;

internal sealed class DebugTeamSwapRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private const int EndpointWheelUnits = 10000;
    private const int EndpointScrollMilliseconds = 280;
    private const int EndpointSettleMilliseconds = 90;
    private const int MiddleDragMilliseconds = 180;
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private TeamSwapCalibration? _sessionCalibration;

    public async Task<DebugRunReport> CheckAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await RunTeamStateAsync(device, cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        TeamSwapLayout? layout = CreateTeamLayout(snapshot);
        return layout is null
            ? MissingRows(snapshot)
            : new DebugRunReport(
                snapshot,
                true,
                "TEAM SWAP TRUE",
                [StateLine(snapshot), LayoutLine(layout)]);
    }

    public async Task<DebugRunReport> LoadAsync(
        int teamNumber,
        string device,
        CancellationToken cancellationToken)
    {
        TeamSwapLayout.ValidateTeamNumber(teamNumber);
        List<string> events = [];
        DebugOcrSnapshot snapshot = await RunTeamStateAsync(device, cancellationToken);
        if (!snapshot.Evaluation.IsMatch)
            return Blocked(snapshot, "TEAM SWAP FALSE", events);
        TeamSwapLayout? liveLayout = CreateTeamLayout(snapshot);
        if (liveLayout is null)
            return Blocked(snapshot, "TEAM BUTTON LAYOUT MISSING", events);
        events.Add(StateLine(snapshot));
        events.Add(LayoutLine(liveLayout));

        bool viewportIsKnownTop = false;
        if (_sessionCalibration is null)
        {
            CalibrationResult? calibrated = await CalibrateAsync(
                liveLayout,
                device,
                events,
                cancellationToken);
            if (calibrated is null)
                return Blocked(snapshot, "TEAM SCROLL CALIBRATION FAILED", events);
            _sessionCalibration = calibrated.Calibration;
            snapshot = calibrated.TopSnapshot;
            liveLayout = CreateTeamLayout(snapshot);
            if (liveLayout is null)
                return Blocked(snapshot, "TEAM TOP LAYOUT MISSING", events);
            viewportIsKnownTop = true;
        }
        else
        {
            events.Add("SESSION SCROLL CALIBRATION REUSED");
        }

        TeamSwapResolvedTarget? target = _sessionCalibration.Resolve(
            teamNumber,
            liveLayout.TitleBounds);
        if (target is null || !IsInside(target.LoadPoint))
        {
            _sessionCalibration = null;
            return Blocked(snapshot, "TEAM CALIBRATION STALE", events);
        }

        if (!viewportIsKnownTop && target.Viewport != TeamSwapViewport.Bottom)
        {
            await ScrollEndpointAsync(
                target.ScrollAnchor,
                downward: false,
                cancellationToken);
            await Task.Delay(EndpointSettleMilliseconds, cancellationToken);
            events.Add(
                $"TOP CLAMP {EndpointWheelUnits} / {EndpointScrollMilliseconds} MS " +
                $"BEFORE {target.Viewport.ToString().ToUpperInvariant()}");
        }

        if (target.Viewport == TeamSwapViewport.Middle)
        {
            if (target.DragStart is null || target.DragEnd is null ||
                !IsInside(target.DragStart.Value) || !IsInside(target.DragEnd.Value))
            {
                _sessionCalibration = null;
                return Blocked(snapshot, "TEAM MIDDLE DRAG INVALID", events);
            }
            await workspace.DragRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                target.DragStart.Value,
                target.DragEnd.Value,
                TimeSpan.FromMilliseconds(MiddleDragMilliseconds),
                cancellationToken);
            events.Add(
                $"CACHED MIDDLE DRAG [{target.DragStart.Value.X},{target.DragStart.Value.Y}] -> " +
                $"[{target.DragEnd.Value.X},{target.DragEnd.Value.Y}] / {MiddleDragMilliseconds} MS");
        }
        else if (target.Viewport == TeamSwapViewport.Bottom)
        {
            await ScrollEndpointAsync(
                target.ScrollAnchor,
                downward: true,
                cancellationToken);
            events.Add($"BOTTOM CLAMP {-EndpointWheelUnits} / {EndpointScrollMilliseconds} MS");
        }

        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            target.LoadPoint,
            cancellationToken);
        events.Add(
            $"TEAM {teamNumber} {target.Viewport.ToString().ToUpperInvariant()} " +
            $"LOAD [{target.LoadPoint.X},{target.LoadPoint.Y}] CENTER");
        DebugRunReport completed = await CompleteLoadAsync(
            teamNumber,
            device,
            events,
            cancellationToken);
        if (!completed.Succeeded)
        {
            _sessionCalibration = null;
            return completed with
            {
                Events = [.. completed.Events, "SESSION SCROLL CALIBRATION INVALIDATED"],
            };
        }
        return completed;
    }

    private async Task<CalibrationResult?> CalibrateAsync(
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

        await ScrollEndpointAsync(initialLayout.ScrollAnchor.Bounds.Center, downward: false, cancellationToken);
        RgbImage topFirst = await CaptureScrollbarAsync(searchRegion, cancellationToken);
        await ScrollEndpointAsync(initialLayout.ScrollAnchor.Bounds.Center, downward: false, cancellationToken);
        RgbImage topSecond = await CaptureScrollbarAsync(searchRegion, cancellationToken);
        DebugOcrSnapshot topSnapshot = await RunTeamStateAsync(device, cancellationToken);
        TeamSwapLayout? topLayout = CreateTeamLayout(topSnapshot);
        if (topLayout is null) return null;

        bool movedDown = false;
        try
        {
            await ScrollEndpointAsync(topLayout.ScrollAnchor.Bounds.Center, downward: true, cancellationToken);
            movedDown = true;
            RgbImage bottomFirst = await CaptureScrollbarAsync(searchRegion, cancellationToken);
            await ScrollEndpointAsync(topLayout.ScrollAnchor.Bounds.Center, downward: true, cancellationToken);
            RgbImage bottomSecond = await CaptureScrollbarAsync(searchRegion, cancellationToken);
            DebugOcrSnapshot bottomSnapshot = await RunTeamStateAsync(device, cancellationToken);
            TeamSwapLayout? bottomLayout = CreateTeamLayout(bottomSnapshot);
            if (bottomLayout is null) return null;

            TeamScrollbarEndpoints? endpoints = TeamScrollbarDetector.TryCalibrate(
                [topFirst, topSecond],
                [bottomFirst, bottomSecond],
                searchRegion);
            if (endpoints is null) return null;
            TeamSwapCalibration? calibration = TeamSwapCalibration.TryCreate(
                DebugWorkflowCatalog.ClientSize,
                topLayout,
                bottomLayout,
                endpoints.TopBounds,
                endpoints.BottomBounds);
            if (calibration is null) return null;
            events.Add(
                $"SCROLLBAR TOP [{endpoints.TopBounds.X},{endpoints.TopBounds.Y}," +
                $"{endpoints.TopBounds.Width},{endpoints.TopBounds.Height}] BOTTOM " +
                $"[{endpoints.BottomBounds.X},{endpoints.BottomBounds.Y}," +
                $"{endpoints.BottomBounds.Width},{endpoints.BottomBounds.Height}] " +
                $"PITCH {calibration.RowPitch}");

            await ScrollEndpointAsync(bottomLayout.ScrollAnchor.Bounds.Center, downward: false, cancellationToken);
            movedDown = false;
            DebugOcrSnapshot restoredTop = await RunTeamStateAsync(device, cancellationToken);
            if (CreateTeamLayout(restoredTop) is null) return null;
            events.Add("SCROLLBAR RESTORED TOP; SESSION CALIBRATION STORED");
            return new CalibrationResult(calibration, restoredTop);
        }
        finally
        {
            if (movedDown)
            {
                try
                {
                    await ScrollEndpointAsync(
                        initialLayout.ScrollAnchor.Bounds.Center,
                        downward: false,
                        CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    // Input cleanup is already complete; a closed or resized client owns the failure.
                }
            }
        }
    }

    private async Task<RgbImage> CaptureScrollbarAsync(
        PixelRect region,
        CancellationToken cancellationToken)
    {
        await Task.Delay(EndpointSettleMilliseconds, cancellationToken);
        IReadOnlyList<LilacMacro.Windows.Capture.CapturedRgbRegion> captures =
            await workspace.CaptureRgbRegionsAsync(
                DebugWorkflowCatalog.ClientSize,
                [region],
                cancellationToken);
        return captures.Single().Image;
    }

    private Task ScrollEndpointAsync(
        PixelPoint anchor,
        bool downward,
        CancellationToken cancellationToken) => workspace.ScrollRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            anchor,
            downward ? -EndpointWheelUnits : EndpointWheelUnits,
            TimeSpan.FromMilliseconds(EndpointScrollMilliseconds),
            cancellationToken);

    private async Task<DebugRunReport> CompleteLoadAsync(
        int teamNumber,
        string device,
        List<string> events,
        CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        DebugStateTransitionObservation confirmTransition = await _states.ObserveTransitionAsync(
            DebugWorkflowCatalog.TeamSwap,
            DebugWorkflowCatalog.TeamLoadConfirm,
            device,
            cancellationToken);
        if (confirmTransition.Outcome != ObservedStateTransitionOutcome.DestinationReached)
        {
            events.Add(StateLine(confirmTransition.Destination));
            return Blocked(
                confirmTransition.Result,
                confirmTransition.Outcome == ObservedStateTransitionOutcome.SourceRetained
                    ? "LOAD TEAM NOT APPLIED"
                    : "LOAD CONFIRM INDETERMINATE",
                events);
        }
        DebugOcrSnapshot confirm = confirmTransition.Destination;
        TeamLoadConfirmLayout? confirmLayout = TeamLoadConfirmLayout.TryCreate(confirm.Regions);
        if (confirmLayout is null)
            return Blocked(confirm, "CONFIRM + CANCEL MISSING", events);
        events.Add(StateLine(confirm));
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            confirmLayout.ConfirmPoint,
            cancellationToken);
        events.Add($"CONFIRM [{confirmLayout.ConfirmPoint.X},{confirmLayout.ConfirmPoint.Y}] CENTER");

        await Task.Delay(250, cancellationToken);
        DebugStateTransitionObservation includeTransition = await _states.ObserveTransitionAsync(
            DebugWorkflowCatalog.TeamLoadConfirm,
            DebugWorkflowCatalog.TeamIncludeEquipment,
            device,
            cancellationToken);
        if (includeTransition.Outcome != ObservedStateTransitionOutcome.DestinationReached)
        {
            events.Add(StateLine(includeTransition.Destination));
            return Blocked(
                includeTransition.Result,
                includeTransition.Outcome == ObservedStateTransitionOutcome.SourceRetained
                    ? "CONFIRM NOT APPLIED"
                    : "INCLUDE EQUIPMENT INDETERMINATE",
                events);
        }
        DebugOcrSnapshot include = includeTransition.Destination;
        TeamIncludeEquipmentLayout? includeLayout =
            TeamIncludeEquipmentLayout.TryCreate(include.Regions);
        if (includeLayout is null)
            return Blocked(include, "INCLUDE GUARDS MISSING", events);
        events.Add(StateLine(include));
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            includeLayout.IncludePoint,
            cancellationToken);
        events.Add($"INCLUDE [{includeLayout.IncludePoint.X},{includeLayout.IncludePoint.Y}] CENTER");

        await Task.Delay(250, cancellationToken);
        DebugStateTransitionObservation completed = await _states.ObserveTransitionAsync(
            DebugWorkflowCatalog.TeamIncludeEquipment,
            DebugWorkflowCatalog.TeamSwap,
            device,
            cancellationToken);
        if (completed.Outcome != ObservedStateTransitionOutcome.DestinationReached)
        {
            events.Add(StateLine(completed.Destination));
            return Blocked(
                completed.Result,
                completed.Outcome == ObservedStateTransitionOutcome.SourceRetained
                    ? "INCLUDE NOT APPLIED"
                    : "TEAM RETURN INDETERMINATE",
                events);
        }
        events.Add(StateLine(completed.Destination));
        return new DebugRunReport(
            completed.Destination,
            true,
            $"TEAM {teamNumber} + EQUIPMENT LOADED",
            events.ToArray());
    }

    private Task<DebugOcrSnapshot> RunTeamStateAsync(
        string device,
        CancellationToken cancellationToken) => _states.RunAsync(
        DebugWorkflowCatalog.TeamSwap,
        device,
        cancellationToken);

    private static TeamSwapLayout? CreateTeamLayout(DebugOcrSnapshot snapshot) =>
        TeamSwapLayout.TryCreate(snapshot.Regions, DebugWorkflowCatalog.ClientSize);

    private static bool IsInside(PixelPoint point) =>
        point.X >= 0 && point.Y >= 0 &&
        point.X < DebugWorkflowCatalog.ClientSize.Width &&
        point.Y < DebugWorkflowCatalog.ClientSize.Height;

    private static DebugRunReport MissingRows(DebugOcrSnapshot snapshot) => new(
        snapshot,
        false,
        "TEAM BUTTON LAYOUT MISSING",
        [StateLine(snapshot), "UNIT TEAMS + TWO SAVE/LOAD ROWS REQUIRED", "INPUT BLOCKED"]);

    private static DebugRunReport Blocked(
        DebugOcrSnapshot snapshot,
        string status,
        IReadOnlyList<string> priorEvents) => new(
        snapshot,
        false,
        status,
        [.. priorEvents, StateLine(snapshot), "INPUT BLOCKED"]);

    private static string LayoutLine(TeamSwapLayout layout) =>
        $"VISIBLE SAVE/LOAD ROWS {layout.Rows.Count} PITCH {layout.RowPitch}";

    private sealed record CalibrationResult(
        TeamSwapCalibration Calibration,
        DebugOcrSnapshot TopSnapshot);
}
