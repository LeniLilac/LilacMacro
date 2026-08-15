using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Runtime;

internal sealed class PlacementPlaybackService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private const int PlacementAttempts = 3;
    private const int KeyHoldMilliseconds = 60;
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly UnitPanelEvidenceService _panel = new(workspace, ocr);
    private readonly MatchTerminalService _terminal = new(workspace, ocr);

    public async Task<PlacementRuntimeResult> RunAsync(
        PlacementSetupDocument document,
        PlacementRouteSetup route,
        PlacementRuntimeKeys keys,
        string device,
        bool repeatStage,
        bool dismissRaidDrops,
        TimeSpan terminalTimeout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        int executed = await RunSetupAsync(
            document, route, keys, device, status, cancellationToken);

        MatchTerminalOutcome outcome = await _terminal.WaitAsync(
            device, terminalTimeout, dismissRaidDrops, status, cancellationToken);
        if (repeatStage)
        {
            await _terminal.RepeatAsync(outcome, device, cancellationToken);
            status?.Invoke("REPEAT STAGE VERIFIED + CLICKED");
        }
        return new PlacementRuntimeResult(outcome, repeatStage, executed);
    }

    public Task RepeatAsync(
        MatchTerminalOutcome outcome,
        string device,
        CancellationToken cancellationToken) =>
        _terminal.RepeatAsync(outcome, device, cancellationToken);

    public async Task<int> RunSetupAsync(
        PlacementSetupDocument document,
        PlacementRouteSetup route,
        PlacementRuntimeKeys keys,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(route);
        PlacementSetupRules.Validate(document);
        PlacementPlaybackPlan plan = PlacementPlaybackPlan.Create(route);
        Dictionary<Guid, PlacementExecutionState> placements = [];
        UnitPanelLayout? layout = null;
        int executed = 0;

        executed += await RunStepsAsync(
            plan.BeforeStart, document, route.BetweenUpgradeAttemptsMilliseconds, keys, device, placements,
            () => layout, value => layout = value, status, cancellationToken);

        await SatisfyStartBoundaryAsync(device, status, cancellationToken);
        status?.Invoke("START GAME VERIFIED + CLICKED");
        executed++;
        await Task.Delay(1000, cancellationToken);

        executed += await RunStepsAsync(
            plan.AfterStart, document, route.BetweenUpgradeAttemptsMilliseconds, keys, device, placements,
            () => layout, value => layout = value, status, cancellationToken);
        return executed;
    }

    public async Task<ExpeditionPlacementSession> RunExpeditionInitialAsync(
        PlacementSetupDocument document,
        PlacementRouteSetup route,
        PlacementRuntimeKeys keys,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(route);
        PlacementSetupRules.Validate(document);
        PlacementPlaybackPlan plan = PlacementPlaybackPlan.Create(route);
        PlacementStep[] steps = [.. plan.BeforeStart, .. plan.AfterStart];
        Dictionary<Guid, PlacementExecutionState> placements = [];
        UnitPanelLayout? layout = null;
        await RunStepsAsync(
            steps, document, route.BetweenUpgradeAttemptsMilliseconds, keys, device, placements,
            () => layout, value => layout = value, status, cancellationToken);
        status?.Invoke("EXPEDITION INITIAL PLACEMENT COMPLETE");
        return new ExpeditionPlacementSession(placements.Values.ToArray(), layout);
    }

    public async Task ReplayExpeditionAsync(
        ExpeditionPlacementSession session,
        PlacementRuntimeKeys keys,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        PlacementExecutionState[] replayCandidates = [.. session.ReplayCandidates];
        if (replayCandidates.Length == 0)
        {
            status?.Invoke("EXPEDITION REPLAY SKIPPED; ALL PLACEMENTS ARE RETAINED PHYSICAL UNITS");
            return;
        }
        QuickPlacementPoint[] batch = replayCandidates
            .Select(state => new QuickPlacementPoint(state.Placement.UnitSlot, state.LivePoint))
            .ToArray();
        await workspace.RunQuickPlacementBatchAsync(
            DebugWorkflowCatalog.ClientSize, keys.QuickPlacement, keys.CancelPlacement, batch, cancellationToken);
        status?.Invoke($"EXPEDITION REPLAY BATCH {batch.Length}");

        if (session.PanelLayout is null)
            throw new InvalidOperationException("Expedition replay has no calibrated unit-panel layout.");
        foreach (PlacementExecutionState saved in replayCandidates)
        {
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize, saved.LivePoint, cancellationToken);
            if (!await _panel.WaitForConfigurableSelectionAsync(
                    session.PanelLayout, device, status, cancellationToken))
            {
                session.MarkRetainedPhysical(saved.Placement.Id);
                status?.Invoke(
                    $"UNIT {saved.Placement.UnitSlot} RETAINED PHYSICAL; FUTURE DEFENSE/ELITE REPLAY SKIPPED");
                continue;
            }

            PlacementExecutionState replacement = new(saved.Placement, saved.LivePoint);
            await ApplyConfigurationAsync(
                replacement, saved.Targeting, saved.AutoUpgrade, keys, cancellationToken);
            await _panel.DismissAsync(session.PanelLayout, status, cancellationToken);
            status?.Invoke($"UNIT {saved.Placement.UnitSlot} PHANTOM REPLACED + CONFIGURED");
        }
    }

    public Task SatisfyExpeditionStartBoundaryAsync(
        ExpeditionPlacementSession session,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        return SatisfyStartBoundaryAsync(
            device,
            status,
            cancellationToken,
            ExpeditionDefenseStartPolicy.PostReplayStartAttempts,
            ExpeditionDefenseStartPolicy.PostReplayRetryMilliseconds);
    }

    private async Task SatisfyStartBoundaryAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken,
        int startAttempts = 20,
        int retryMilliseconds = 250)
    {
        for (int attempt = 1; attempt <= startAttempts; attempt++)
        {
            DebugRunReport start = await _debug.StartGameAsync(device, cancellationToken);
            if (start.Succeeded) return;
            status?.Invoke($"START SCREEN ABSENT {attempt}/{startAttempts}");
            if (attempt < startAttempts)
            {
                await Task.Delay(retryMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Start Game did not expose a verified action after {startAttempts} fresh observation(s).");
    }

    private async Task<int> RunStepsAsync(
        IReadOnlyList<PlacementStep> steps,
        PlacementSetupDocument document,
        int betweenUpgradeAttemptsMilliseconds,
        PlacementRuntimeKeys keys,
        string device,
        Dictionary<Guid, PlacementExecutionState> placements,
        Func<UnitPanelLayout?> getLayout,
        Action<UnitPanelLayout> setLayout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        int executed = 0;
        foreach (PlacementPlaybackGroup group in PlacementPlaybackPlan.Group(steps))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Kind == PlacementStepKind.Place)
            {
                await RunPlacementGroupAsync(
                    group.Steps, document, keys, device, placements,
                    getLayout, setLayout, status, cancellationToken);
                executed += group.Steps.Count;
                continue;
            }
            PlacementStep step = group.Steps[0];
            await RunActionAsync(
                step, betweenUpgradeAttemptsMilliseconds, keys, device, placements,
                getLayout, status, cancellationToken);
            executed++;
        }
        return executed;
    }

    private async Task RunPlacementGroupAsync(
        IReadOnlyList<PlacementStep> steps,
        PlacementSetupDocument document,
        PlacementRuntimeKeys keys,
        string device,
        Dictionary<Guid, PlacementExecutionState> placements,
        Func<UnitPanelLayout?> getLayout,
        Action<UnitPanelLayout> setLayout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        QuickPlacementPoint[] batch = steps.Select(step => ToQuickPoint(step, document)).ToArray();
        await workspace.RunQuickPlacementBatchAsync(
            DebugWorkflowCatalog.ClientSize, keys.QuickPlacement, keys.CancelPlacement, batch, cancellationToken);
        status?.Invoke($"QUICK PLACEMENT BATCH {batch.Length}");

        foreach (PlacementStep step in steps)
        {
            QuickPlacementPoint point = ToQuickPoint(step, document);
            UnitPanelLayout? layout = getLayout();
            bool selected = false;
            for (int attempt = 1; attempt <= PlacementAttempts && !selected; attempt++)
            {
                if (attempt > 1)
                {
                    status?.Invoke($"RETRY PLACEMENT {attempt}/{PlacementAttempts} UNIT {step.UnitSlot}");
                    await workspace.RunQuickPlacementBatchAsync(
                        DebugWorkflowCatalog.ClientSize, keys.QuickPlacement, keys.CancelPlacement, [point], cancellationToken);
                }
                await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point.Point, cancellationToken);
                if (layout is null)
                {
                    try
                    {
                        layout = await _panel.CalibrateAsync(device, status, cancellationToken);
                        setLayout(layout);
                        selected = true;
                    }
                    catch (InvalidOperationException error) when (attempt < PlacementAttempts)
                    {
                        status?.Invoke(error.Message);
                    }
                }
                else
                {
                    selected = await _panel.WaitForConfigurableSelectionAsync(layout, device, status, cancellationToken);
                }
            }
            if (!selected || layout is null)
                throw new InvalidOperationException($"Unit {step.UnitSlot} at {point.Point} did not produce configurable selection proof.");

            PlacementExecutionState state = new(step, point.Point);
            placements.Add(step.Id, state);
            await ApplyConfigurationAsync(state, step.TargetingPriority, step.AutoUpgradePriority, keys, cancellationToken);
            await _panel.DismissAsync(layout, status, cancellationToken);
        }
    }

    private async Task RunActionAsync(
        PlacementStep step,
        int betweenUpgradeAttemptsMilliseconds,
        PlacementRuntimeKeys keys,
        string device,
        Dictionary<Guid, PlacementExecutionState> placements,
        Func<UnitPanelLayout?> getLayout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (step.Kind == PlacementStepKind.Delay)
        {
            await Task.Delay(step.DelayDurationMilliseconds, cancellationToken);
            return;
        }
        UnitPanelLayout layout = getLayout() ?? throw new InvalidOperationException("Unit panel layout was not calibrated.");
        PlacementExecutionState target = ResolveTarget(step, placements);
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, target.LivePoint, cancellationToken);
        bool selected = UnitPanelSelectionPolicy.AllowsPhantom(step.Kind)
            ? await _panel.WaitForConfigurableSelectionAsync(layout, device, status, cancellationToken)
            : await _panel.WaitForPhysicalSelectionAsync(layout, device, status, cancellationToken);
        if (!selected)
        {
            string required = UnitPanelSelectionPolicy.RequiresPhysical(step.Kind) ? "physical" : "configurable";
            throw new InvalidOperationException($"{step.Kind} target did not produce {required} selection proof.");
        }

        switch (step.Kind)
        {
            case PlacementStepKind.Reconfigure:
                await ApplyReconfigureAsync(target, step, keys, cancellationToken);
                break;
            case PlacementStepKind.Upgrade:
                await ApplyUpgradesAsync(
                    layout, step.UpgradeCount, betweenUpgradeAttemptsMilliseconds,
                    keys, device, status, cancellationToken);
                break;
            case PlacementStepKind.Sell:
                await TapAsync(keys.Sell, keys.ReservedVirtualKey, 1, cancellationToken);
                if (!await _panel.WaitForPanelHiddenAsync(layout, cancellationToken))
                    throw new InvalidOperationException("Sell did not close the selected-unit panel.");
                placements.Remove(target.Placement.Id);
                break;
            default:
                throw new InvalidDataException($"Unsupported playback step {step.Kind}.");
        }
        if (UnitPanelDismissalPolicy.RequiresDismissal(step.Kind))
            await _panel.DismissAsync(layout, status, cancellationToken);
    }

    private async Task ApplyUpgradesAsync(
        UnitPanelLayout layout,
        int count,
        int betweenAttemptsMilliseconds,
        PlacementRuntimeKeys keys,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        foreach (UnitUpgradeAttempt attempt in UnitUpgradeAttemptSchedule.Create(
                     count, betweenAttemptsMilliseconds))
        {
            if (attempt.DelayBeforeMilliseconds > 0)
                await Task.Delay(attempt.DelayBeforeMilliseconds, cancellationToken);
            UnitUpgradeState state = await _panel.WaitForUpgradeAsync(
                layout, device, status, cancellationToken);
            if (state == UnitUpgradeState.Maxed)
            {
                status?.Invoke($"UNIT MAXED; SKIPPED {count - attempt.Number + 1} UPGRADE PRESS(ES)");
                return;
            }
            await TapAsync(keys.Upgrade, keys.ReservedVirtualKey, 1, cancellationToken);
        }
    }

    private async Task ApplyReconfigureAsync(
        PlacementExecutionState state,
        PlacementStep step,
        PlacementRuntimeKeys keys,
        CancellationToken cancellationToken)
    {
        PlacementTargetingPriority targeting = step.ChangeTargetingPriority ? step.TargetingPriority : state.Targeting;
        PlacementAutoUpgradePriority auto = ToPriority(step.AutoUpgradeAction, state.AutoUpgrade);
        await ApplyConfigurationAsync(state, targeting, auto, keys, cancellationToken);
    }

    private async Task ApplyConfigurationAsync(
        PlacementExecutionState state,
        PlacementTargetingPriority targeting,
        PlacementAutoUpgradePriority auto,
        PlacementRuntimeKeys keys,
        CancellationToken cancellationToken)
    {
        int targetingCount = Enum.GetValues<PlacementTargetingPriority>().Length;
        int targetTaps = ((int)targeting - (int)state.Targeting + targetingCount) % targetingCount;
        int autoCount = Enum.GetValues<PlacementAutoUpgradePriority>().Length;
        int autoTaps = ((int)auto - (int)state.AutoUpgrade + autoCount) % autoCount;
        await TapAsync(keys.ChangeTargeting, keys.ReservedVirtualKey, targetTaps, cancellationToken);
        await TapAsync(keys.ChangeAutoUpgrade, keys.ReservedVirtualKey, autoTaps, cancellationToken);
        state.Targeting = targeting;
        state.AutoUpgrade = auto;
    }

    private async Task TapAsync(
        int virtualKey,
        int reservedVirtualKey,
        int count,
        CancellationToken cancellationToken)
    {
        while (count > 0)
        {
            int chunk = Math.Min(count, 32);
            AutomationKeyPress[] presses = Enumerable.Range(0, chunk)
                .Select(_ => AutomationKeyPress.Create(
                    virtualKey,
                    KeyHoldMilliseconds,
                    reservedVirtualKey))
                .ToArray();
            await workspace.RunKeySequenceAsync(
                DebugWorkflowCatalog.ClientSize, AutomationKeySequence.Create(presses), cancellationToken);
            count -= chunk;
        }
    }

    private static PlacementExecutionState ResolveTarget(
        PlacementStep step,
        IReadOnlyDictionary<Guid, PlacementExecutionState> placements) =>
        step.TargetPlacementId is Guid id && placements.TryGetValue(id, out PlacementExecutionState? target)
            ? target
            : throw new InvalidOperationException("Unit action target is not an active earlier placement.");

    private static PlacementAutoUpgradePriority ToPriority(
        PlacementAutoUpgradeAction action,
        PlacementAutoUpgradePriority current) => action switch
        {
            PlacementAutoUpgradeAction.NoChange => current,
            PlacementAutoUpgradeAction.Disable => PlacementAutoUpgradePriority.Off,
            PlacementAutoUpgradeAction.Priority1 => PlacementAutoUpgradePriority.Priority1,
            PlacementAutoUpgradeAction.Priority2 => PlacementAutoUpgradePriority.Priority2,
            PlacementAutoUpgradeAction.Priority3 => PlacementAutoUpgradePriority.Priority3,
            PlacementAutoUpgradeAction.Priority4 => PlacementAutoUpgradePriority.Priority4,
            PlacementAutoUpgradeAction.Priority5 => PlacementAutoUpgradePriority.Priority5,
            PlacementAutoUpgradeAction.Priority6 => PlacementAutoUpgradePriority.Priority6,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    private static QuickPlacementPoint ToQuickPoint(PlacementStep step, PlacementSetupDocument document) => new(
        step.UnitSlot,
        new PixelPoint(
            Math.Clamp((int)Math.Round(step.X * DebugWorkflowCatalog.ClientSize.Width / (double)document.ImageWidth), 0,
                DebugWorkflowCatalog.ClientSize.Width - 1),
            Math.Clamp((int)Math.Round(step.Y * DebugWorkflowCatalog.ClientSize.Height / (double)document.ImageHeight), 0,
                DebugWorkflowCatalog.ClientSize.Height - 1)));

}
