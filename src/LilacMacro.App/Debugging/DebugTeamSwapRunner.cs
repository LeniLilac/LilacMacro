using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
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
    private const int MiddleScrollMilliseconds = 280;
    private const int MaximumLoadAttempts = 2;
    private const int MaximumStateObservations = 3;
    private static readonly TimeSpan StateRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly TeamSwapScrollCalibrator _scrollCalibrator = new(workspace, ocr);
    private TeamSwapScrollCalibrationResult? _sessionCalibration;

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
            TeamSwapScrollCalibrationResult? calibrated = await _scrollCalibrator.CalibrateAsync(
                liveLayout,
                device,
                events,
                cancellationToken);
            if (calibrated is null)
                return Blocked(snapshot, "TEAM SCROLL CALIBRATION FAILED", events);
            _sessionCalibration = calibrated;
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

        TeamSwapResolvedTarget? target = _sessionCalibration.Calibration.Resolve(
            teamNumber,
            liveLayout.TitleBounds);
        if (target is null || !IsInside(target.LoadPoint))
        {
            events.Add("SESSION SCROLL CALIBRATION STALE; RECALIBRATING");
            _sessionCalibration = null;
            TeamSwapScrollCalibrationResult? recalibrated = await _scrollCalibrator.CalibrateAsync(
                liveLayout,
                device,
                events,
                cancellationToken);
            if (recalibrated is null)
                return Blocked(snapshot, "TEAM RECALIBRATION FAILED", events);
            _sessionCalibration = recalibrated;
            snapshot = recalibrated.TopSnapshot;
            liveLayout = CreateTeamLayout(snapshot);
            target = liveLayout is null
                ? null
                : recalibrated.Calibration.Resolve(teamNumber, liveLayout.TitleBounds);
            if (target is null || !IsInside(target.LoadPoint))
            {
                _sessionCalibration = null;
                return Blocked(snapshot, "TEAM RECALIBRATION STALE", events);
            }
            viewportIsKnownTop = true;
        }

        for (int attempt = 1; attempt <= MaximumLoadAttempts; attempt++)
        {
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
                if (target.MiddleWheelUnits <= 0)
                {
                    _sessionCalibration = null;
                    return Blocked(snapshot, "TEAM MIDDLE SCROLL INVALID", events);
                }
                await workspace.ScrollRobloxAsync(
                    DebugWorkflowCatalog.ClientSize,
                    target.ScrollAnchor,
                    -target.MiddleWheelUnits,
                    TimeSpan.FromMilliseconds(MiddleScrollMilliseconds),
                    cancellationToken);
                events.Add(
                    $"CACHED MIDDLE SCROLL {-target.MiddleWheelUnits} / " +
                    $"{MiddleScrollMilliseconds} MS");
                TeamScrollbarObservation? observation = await _scrollCalibrator.ObserveAsync(
                    _sessionCalibration,
                    liveLayout!.TitleBounds,
                    cancellationToken);
                if (observation is null ||
                    !TeamSwapCalibration.IsMiddlePositionUsable(observation.NormalizedPosition))
                {
                    _sessionCalibration = null;
                    string position = observation is null
                        ? "NOT FOUND"
                        : observation.NormalizedPosition.ToString("P2");
                    events.Add($"MIDDLE THUMB {position}; EXPECTED 40%-60%");
                    return Blocked(snapshot, "TEAM MIDDLE SCROLL NOT VERIFIED", events);
                }
                target = _sessionCalibration.Calibration.Resolve(
                    teamNumber,
                    liveLayout.TitleBounds,
                    observation.NormalizedPosition);
                if (target is null || !IsInside(target.LoadPoint))
                {
                    _sessionCalibration = null;
                    return Blocked(snapshot, "TEAM MIDDLE LOAD OUTSIDE VIEW", events);
                }
                events.Add(
                    $"MIDDLE THUMB {observation.NormalizedPosition:P2} " +
                    $"[{observation.Bounds.X},{observation.Bounds.Y}," +
                    $"{observation.Bounds.Width},{observation.Bounds.Height}]");
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
                $"LOAD [{target.LoadPoint.X},{target.LoadPoint.Y}] " +
                $"ATTEMPT {attempt}/{MaximumLoadAttempts}");
            LoadCompletion completion = await CompleteLoadAsync(
                teamNumber,
                device,
                target.ScrollAnchor,
                events,
                cancellationToken);
            if (!completion.RetryRequested)
            {
                if (!completion.Report.Succeeded)
                {
                    return completion.Report with
                    {
                        Events = [.. completion.Report.Events,
                            "SESSION SCROLL CALIBRATION RETAINED"],
                    };
                }
                return completion.Report;
            }

            if (attempt == MaximumLoadAttempts)
            {
                return completion.Report with
                {
                    Status = $"{completion.Report.Status}; RETRY LIMIT",
                    Events = [.. completion.Report.Events,
                        "LOAD RETRY LIMIT 2", "INPUT BLOCKED",
                        "SESSION SCROLL CALIBRATION RETAINED"],
                };
            }

            await ScrollEndpointAsync(target.ScrollAnchor, downward: false, cancellationToken);
            await Task.Delay(EndpointSettleMilliseconds, cancellationToken);
            events.Add($"LOAD RETRY TOP CLAMP {EndpointWheelUnits} / {EndpointScrollMilliseconds} MS");
            snapshot = await RunTeamStateAsync(device, cancellationToken);
            liveLayout = CreateTeamLayout(snapshot);
            TeamSwapResolvedTarget? freshTarget = liveLayout is null
                ? null
                : _sessionCalibration.Calibration.Resolve(teamNumber, liveLayout.TitleBounds);
            TeamScrollbarObservation? topObservation = liveLayout is null
                ? null
                : await _scrollCalibrator.ObserveAsync(
                    _sessionCalibration,
                    liveLayout.TitleBounds,
                    cancellationToken);
            TeamSwapRetryGeometryDecision retryDecision =
                TeamSwapCalibration.DecideRetryGeometry(
                    snapshot.Evaluation.IsMatch,
                    liveLayout is not null,
                    topObservation is not null && TeamSwapCalibration.IsTopPositionUsable(
                        topObservation.NormalizedPosition),
                    freshTarget is not null && IsInside(freshTarget.LoadPoint));
            if (retryDecision == TeamSwapRetryGeometryDecision.Block)
            {
                return Blocked(
                    snapshot,
                    liveLayout is null ? "TEAM RETRY LAYOUT MISSING" : "TEAM RETRY SOURCE INDETERMINATE",
                    [.. events, "SESSION SCROLL CALIBRATION RETAINED"]);
            }
            if (retryDecision == TeamSwapRetryGeometryDecision.Recalibrate)
            {
                events.Add("RETRY GEOMETRY FAILED VALIDATION; RECALIBRATING");
                _sessionCalibration = await _scrollCalibrator.CalibrateAsync(
                    liveLayout!,
                    device,
                    events,
                    cancellationToken);
                if (_sessionCalibration is null)
                    return Blocked(snapshot, "TEAM RETRY CALIBRATION FAILED", events);
                snapshot = _sessionCalibration.TopSnapshot;
                liveLayout = CreateTeamLayout(snapshot);
                freshTarget = liveLayout is null
                    ? null
                    : _sessionCalibration.Calibration.Resolve(teamNumber, liveLayout.TitleBounds);
                events.Add("SESSION SCROLL CALIBRATION REFRESHED AFTER GEOMETRY FAILURE");
            }
            else
            {
                events.Add($"RETRY TOP THUMB {topObservation!.NormalizedPosition:P2}");
                events.Add("FRESH LOAD TARGET RESOLVED; SESSION SCROLL CALIBRATION RETAINED");
            }
            target = freshTarget;
            if (target is null || !IsInside(target.LoadPoint))
            {
                _sessionCalibration = null;
                return Blocked(snapshot, "TEAM RETRY CALIBRATION STALE", events);
            }
            viewportIsKnownTop = true;
            events.Add($"LOAD TEAM RETRY {attempt + 1}/{MaximumLoadAttempts}");
        }

        throw new InvalidOperationException("The bounded load attempt loop did not return.");
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

    private async Task<LoadCompletion> CompleteLoadAsync(
        int teamNumber,
        string device,
        PixelPoint scrollAnchor,
        List<string> events,
        CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        DebugOcrSnapshot confirm = await _states.RunAsync(
            DebugWorkflowCatalog.TeamLoadConfirm,
            device,
            cancellationToken);
        if (!confirm.Evaluation.IsMatch)
        {
            DebugOcrSnapshot save = await _states.RunAsync(
                DebugWorkflowCatalog.TeamSaveConfirm,
                device,
                cancellationToken);
            if (save.Evaluation.IsMatch)
            {
                return await RecoverFromSaveAsync(
                    save,
                    device,
                    scrollAnchor,
                    events,
                    cancellationToken);
            }

            events.Add(StateLine(confirm));
            events.Add(StateLine(save));
            DebugOcrSnapshot source = await RunTeamStateAsync(device, cancellationToken);
            return new LoadCompletion(
                Blocked(
                    source,
                    source.Evaluation.IsMatch
                        ? "LOAD TEAM NOT APPLIED"
                        : "LOAD CONFIRM INDETERMINATE",
                    events),
                RetryRequested: source.Evaluation.IsMatch);
        }
        TeamLoadConfirmLayout? confirmLayout = TeamLoadConfirmLayout.TryCreate(confirm.Regions);
        if (confirmLayout is null)
        {
            return new LoadCompletion(
                Blocked(confirm, "LOAD TEAM + CONFIRM + CANCEL MISSING", events),
                RetryRequested: false);
        }
        events.Add(StateLine(confirm));
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            confirmLayout.ConfirmPoint,
            cancellationToken);
        events.Add($"CONFIRM [{confirmLayout.ConfirmPoint.X},{confirmLayout.ConfirmPoint.Y}] CENTER");

        await Task.Delay(250, cancellationToken);
        DebugStateTransitionObservation includeTransition = await _states.WaitForTransitionAsync(
            DebugWorkflowCatalog.TeamLoadConfirm,
            DebugWorkflowCatalog.TeamIncludeEquipment,
            device,
            MaximumStateObservations,
            StateRetryDelay,
            cancellationToken);
        if (includeTransition.Outcome != ObservedStateTransitionOutcome.DestinationReached)
        {
            events.Add(StateLine(includeTransition.Destination));
            return new LoadCompletion(
                Blocked(
                    includeTransition.Result,
                    includeTransition.Outcome == ObservedStateTransitionOutcome.SourceRetained
                        ? "CONFIRM NOT APPLIED"
                        : "INCLUDE EQUIPMENT INDETERMINATE",
                    events),
                RetryRequested: false);
        }
        DebugOcrSnapshot include = includeTransition.Destination;
        TeamIncludeEquipmentLayout? includeLayout =
            TeamIncludeEquipmentLayout.TryCreate(include.Regions);
        if (includeLayout is null)
        {
            return new LoadCompletion(
                Blocked(include, "INCLUDE GUARDS MISSING", events),
                RetryRequested: false);
        }
        events.Add(StateLine(include));
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            includeLayout.IncludePoint,
            cancellationToken);
        events.Add($"INCLUDE [{includeLayout.IncludePoint.X},{includeLayout.IncludePoint.Y}] CENTER");

        await Task.Delay(250, cancellationToken);
        DebugStateTransitionObservation completed = await _states.WaitForTransitionAsync(
            DebugWorkflowCatalog.TeamIncludeEquipment,
            DebugWorkflowCatalog.TeamSwap,
            device,
            MaximumStateObservations,
            StateRetryDelay,
            cancellationToken);
        if (completed.Outcome != ObservedStateTransitionOutcome.DestinationReached)
        {
            events.Add(StateLine(completed.Destination));
            return new LoadCompletion(
                Blocked(
                    completed.Result,
                    completed.Outcome == ObservedStateTransitionOutcome.SourceRetained
                        ? "INCLUDE NOT APPLIED"
                        : "TEAM RETURN INDETERMINATE",
                    events),
                RetryRequested: false);
        }
        events.Add(StateLine(completed.Destination));
        return new LoadCompletion(
            new DebugRunReport(
                completed.Destination,
                true,
                $"TEAM {teamNumber} + EQUIPMENT LOADED",
                events.ToArray()),
            RetryRequested: false);
    }

    private async Task<LoadCompletion> RecoverFromSaveAsync(
        DebugOcrSnapshot save,
        string device,
        PixelPoint scrollAnchor,
        List<string> events,
        CancellationToken cancellationToken)
    {
        TeamLoadConfirmLayout? saveLayout = TeamLoadConfirmLayout.TryCreate(save.Regions);
        if (saveLayout is null)
        {
            return new LoadCompletion(
                Blocked(save, "SAVE TEAM CANCEL MISSING", events),
                RetryRequested: false);
        }

        events.Add(StateLine(save));
        events.Add("SAVE TEAM NEGATIVE EVIDENCE; CONFIRM BLOCKED");
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            saveLayout.CancelPoint,
            cancellationToken);
        events.Add($"SAVE CANCEL [{saveLayout.CancelPoint.X},{saveLayout.CancelPoint.Y}] CENTER");

        await Task.Delay(250, cancellationToken);
        DebugOcrSnapshot lingering = await _states.RunAsync(
            DebugWorkflowCatalog.TeamSaveConfirm,
            device,
            cancellationToken);
        if (lingering.Evaluation.IsMatch)
        {
            return new LoadCompletion(
                Blocked(lingering, "SAVE CANCEL NOT APPLIED", events),
                RetryRequested: false);
        }

        await ScrollEndpointAsync(scrollAnchor, downward: false, cancellationToken);
        await Task.Delay(EndpointSettleMilliseconds, cancellationToken);
        events.Add($"SAVE RECOVERY TOP CLAMP {EndpointWheelUnits} / {EndpointScrollMilliseconds} MS");
        DebugOcrSnapshot restored = await RunTeamStateAsync(device, cancellationToken);
        if (!restored.Evaluation.IsMatch || CreateTeamLayout(restored) is null)
        {
            return new LoadCompletion(
                Blocked(restored, "TEAM TOP NOT RESTORED", events),
                RetryRequested: false);
        }

        events.Add(StateLine(restored));
        return new LoadCompletion(
            new DebugRunReport(
                restored,
                false,
                "SAVE TEAM CANCELLED; RETRYING LOAD",
                events.ToArray()),
            RetryRequested: true);
    }

    private Task<DebugOcrSnapshot> RunTeamStateAsync(
        string device,
        CancellationToken cancellationToken) => _states.WaitForMatchAsync(
        DebugWorkflowCatalog.TeamSwap,
        device,
        MaximumStateObservations,
        StateRetryDelay,
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

    private sealed record LoadCompletion(
        DebugRunReport Report,
        bool RetryRequested);
}
