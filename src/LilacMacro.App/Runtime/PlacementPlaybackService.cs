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
    private const int KeyHoldMilliseconds = 60;
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly UnitPanelEvidenceService _panel = new(workspace, ocr);
    private readonly MatchTerminalService _terminal = new(workspace, ocr);
    private TerminalAwarePlacementSetup TerminalAware => new(_terminal);

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
        TerminalAwarePlacementSetupResult setup = await RunSetupCoreAsync(
            document, route, keys, device, status, cancellationToken);

        MatchTerminalOutcome outcome = setup.TerminalOutcome ?? await _terminal.WaitAsync(
            device, terminalTimeout, dismissRaidDrops, status, cancellationToken);
        if (repeatStage)
        {
            await _terminal.RepeatAsync(outcome, device, cancellationToken);
            status?.Invoke("REPEAT STAGE VERIFIED + CLICKED");
        }
        return new PlacementRuntimeResult(outcome, repeatStage, setup.ExecutedSteps);
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
        return (await RunSetupCoreAsync(
            document, route, keys, device, status, cancellationToken, monitorTerminal: false))
            .ExecutedSteps;
    }

    private async Task<TerminalAwarePlacementSetupResult> RunSetupCoreAsync(
        PlacementSetupDocument document,
        PlacementRouteSetup route,
        PlacementRuntimeKeys keys,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken,
        bool monitorTerminal = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(route);
        PlacementSetupRules.Validate(document);
        PlacementPlaybackPlan plan = PlacementPlaybackPlan.Create(route);
        Dictionary<Guid, PlacementExecutionState> placements = [];
        UnitPanelLayout? layout = null;
        PlacementSetupExecution execution = new();

        int beforeStart = await RunStepsAsync(
            plan.BeforeStart, document, route.BetweenUpgradeAttemptsMilliseconds, keys, device, placements,
            () => layout, value => layout = value, status, cancellationToken, null);
        execution.ExecutedSteps = beforeStart;

        await TerminalAware.SatisfyStartBoundaryAsync(
            device, status, token => _debug.StartGameAsync(device, token), cancellationToken);
        status?.Invoke("START GAME VERIFIED + CLICKED");
        execution.ExecutedSteps++;

        async Task<int> RunAfterStartAsync(CancellationToken token)
        {
            await Task.Delay(1000, token);
            return await RunStepsAsync(
                plan.AfterStart, document, route.BetweenUpgradeAttemptsMilliseconds, keys, device, placements,
                () => layout, value => layout = value, status, token, execution);
        }

        if (!monitorTerminal)
        {
            await RunAfterStartAsync(cancellationToken);
            return new TerminalAwarePlacementSetupResult(execution.ExecutedSteps, null);
        }

        return await TerminalAware.RunAsync(
            execution, device, status, RunAfterStartAsync, cancellationToken);
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
            () => layout, value => layout = value, status, cancellationToken, null);
        status?.Invoke("EXPEDITION INITIAL PLACEMENT COMPLETE");
        return new ExpeditionPlacementSession(placements.Values.ToArray(), layout);
    }

    public async Task ReplayExpeditionAsync(
        ExpeditionPlacementSession session,
        PlacementRuntimeKeys keys,
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
            if (!await _panel.WaitForSelectedPanelAsync(
                    session.PanelLayout, status, cancellationToken))
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
            status?.Invoke($"UNIT {saved.Placement.UnitSlot} REPLAYED + CONFIGURED");
        }
    }

    public Task SatisfyExpeditionStartBoundaryAsync(
        ExpeditionPlacementSession session,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        return TerminalAware.SatisfyStartBoundaryAsync(
            device, status, token => _debug.StartGameAsync(device, token), cancellationToken);
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
        CancellationToken cancellationToken,
        PlacementSetupExecution? execution)
    {
        int executed = 0;
        foreach (PlacementPlaybackGroup group in PlacementPlaybackPlan.Group(steps))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Kind == PlacementStepKind.Place)
            {
                executed += await RunPlacementGroupAsync(
                    group.Steps, document, keys, device, placements,
                    getLayout, setLayout, status, cancellationToken, execution);
                continue;
            }
            PlacementStep step = group.Steps[0];
            if (await RunActionAsync(
                    step, betweenUpgradeAttemptsMilliseconds, keys, device, placements,
                    getLayout, status, cancellationToken))
            {
                executed++;
                execution?.CountStep();
            }
        }
        return executed;
    }

    private async Task<int> RunPlacementGroupAsync(
        IReadOnlyList<PlacementStep> steps,
        PlacementSetupDocument document,
        PlacementRuntimeKeys keys,
        string device,
        Dictionary<Guid, PlacementExecutionState> placements,
        Func<UnitPanelLayout?> getLayout,
        Action<UnitPanelLayout> setLayout,
        Action<string>? status,
        CancellationToken cancellationToken,
        PlacementSetupExecution? execution)
    {
        int executed = 0;
        QuickPlacementPoint[] batch = steps.Select(step => ToQuickPoint(step, document)).ToArray();
        await workspace.RunQuickPlacementBatchAsync(
            DebugWorkflowCatalog.ClientSize, keys.QuickPlacement, keys.CancelPlacement, batch, cancellationToken);
        status?.Invoke($"QUICK PLACEMENT BATCH {batch.Length}");

        foreach (PlacementStep step in steps)
        {
            QuickPlacementPoint point = ToQuickPoint(step, document);
            UnitPanelLayout? layout = getLayout();
            bool selected = false;
            for (int attempt = 1; attempt <= PlacementSelectionRetryPolicy.MaximumAttempts && !selected; attempt++)
            {
                if (attempt > 1)
                {
                    status?.Invoke(
                        $"RETRY PLACEMENT {attempt}/{PlacementSelectionRetryPolicy.MaximumAttempts} UNIT {step.UnitSlot}");
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
                    catch (InvalidOperationException error)
                    {
                        status?.Invoke(error.Message);
                    }
                }
                else
                {
                    selected = await _panel.WaitForSelectedPanelAsync(layout, status, cancellationToken);
                }

                if (!selected && layout is not null)
                    await _panel.DismissAsync(layout, status, cancellationToken);
            }
            if (!selected || layout is null)
            {
                status?.Invoke(
                    $"SKIPPED PLACE STEP; CONFIGURABLE SELECTION PROOF FAILED AFTER " +
                    $"{PlacementSelectionRetryPolicy.MaximumAttempts} ATTEMPTS UNIT {step.UnitSlot}");
                continue;
            }

            PlacementExecutionState state = new(step, point.Point);
            placements.Add(step.Id, state);
            await ApplyConfigurationAsync(state, step.TargetingPriority, step.AutoUpgradePriority, keys, cancellationToken);
            await _panel.DismissAsync(layout, status, cancellationToken);
            executed++;
            execution?.CountStep();
        }
        return executed;
    }

    private async Task<bool> RunActionAsync(
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
            return true;
        }

        UnitPanelLayout? layout = getLayout();
        if (layout is null)
        {
            status?.Invoke($"SKIPPED {step.Kind.ToString().ToUpperInvariant()} STEP; UNIT PANEL LAYOUT UNAVAILABLE");
            return false;
        }

        if (step.TargetPlacementId is not Guid targetId ||
            !placements.TryGetValue(targetId, out PlacementExecutionState? target))
        {
            status?.Invoke($"SKIPPED {step.Kind.ToString().ToUpperInvariant()} STEP; TARGET PLACEMENT UNAVAILABLE");
            return false;
        }

        bool selected = await TrySelectTargetAsync(
            step, target.LivePoint, layout, device, status, cancellationToken);
        if (!selected)
        {
            string required = UnitPanelSelectionPolicy.RequiresPhysicalDpsEvidence(step.Kind)
                ? "physical"
                : "selected-panel";
            status?.Invoke(
                $"SKIPPED {step.Kind.ToString().ToUpperInvariant()} STEP; {required.ToUpperInvariant()} " +
                $"SELECTION PROOF FAILED AFTER {PlacementSelectionRetryPolicy.MaximumAttempts} ATTEMPTS");
            return false;
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
        return true;
    }

    private async Task<bool> TrySelectTargetAsync(
        PlacementStep step,
        PixelPoint point,
        UnitPanelLayout layout,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= PlacementSelectionRetryPolicy.MaximumAttempts; attempt++)
        {
            if (attempt > 1)
            {
                status?.Invoke(
                    $"RESELECT {step.Kind.ToString().ToUpperInvariant()} TARGET " +
                    $"{attempt}/{PlacementSelectionRetryPolicy.MaximumAttempts}");
            }

            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize, point, cancellationToken);
            bool selected = UnitPanelSelectionPolicy.RequiresPhysicalDpsEvidence(step.Kind)
                ? await _panel.WaitForPhysicalSelectionAsync(layout, device, status, cancellationToken)
                : await _panel.WaitForSelectedPanelAsync(layout, status, cancellationToken);
            if (selected) return true;

            await _panel.DismissAsync(layout, status, cancellationToken);
            if (!PlacementSelectionRetryPolicy.ShouldRetry(attempt)) break;
        }

        return false;
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
