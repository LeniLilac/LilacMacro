using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using static LilacMacro.App.Debugging.DebugReportFactory;

namespace LilacMacro.App.Debugging;

internal sealed class EventWireNavigator(WorkspaceController workspace, OcrRunner ocr)
{
    private static readonly PixelPoint ScrollAnchor = new(1349, 561);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly ObservedStateTransitionRunner _transitions = new(workspace, ocr);

    public async Task<DebugRunReport> OpenVillainActsAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.EventPageConfirm,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);
        OcrTargetMatch? target = snapshot.Evaluation.Matches.FirstOrDefault(match =>
            match.Target.Equals("Event Gamemode", StringComparison.Ordinal));
        if (target is null) return MissingTarget(snapshot, "EVENT GAMEMODE");

        PixelPoint point = target.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return ClickReport(snapshot, target, point, "CENTER");
    }

    public async Task<DebugRunReport> SelectActAndStageAsync(
        StoryAct act,
        string device,
        CancellationToken cancellationToken)
    {
        List<string> events = [];
        if (EventRunPolicy.RequiresActScroll(act))
        {
            await workspace.ScrollRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                ScrollAnchor,
                -2000,
                TimeSpan.FromSeconds(2),
                cancellationToken);
            await Task.Delay(250, cancellationToken);
            events.Add("ACT CARDS SCROLL -2000 / 2000 MS");
        }

        OcrTargetRule actRule = EventRunPolicy.TargetFor(act);
        ObservedStateTransitionRunResult actTransition = await _transitions.RunAsync(
            DebugWorkflowCatalog.EventActPicker,
            DebugWorkflowCatalog.EventStagePreview,
            device,
            token => ClickActAsync(actRule, device, token),
            cancellationToken);
        if (!actTransition.Succeeded)
            return TransitionFailed(actTransition, "EVENT ACT -> STAGE PREVIEW", events);

        ObservedStateTransitionRunResult stageTransition = await _transitions.RunAsync(
            DebugWorkflowCatalog.EventStagePreview,
            DebugWorkflowCatalog.MatchPreview,
            device,
            token => ClickSelectStageAsync(device, token),
            cancellationToken);
        if (!stageTransition.Succeeded)
            return TransitionFailed(stageTransition, "EVENT STAGE -> MATCH PREVIEW", events);

        return new DebugRunReport(
            stageTransition.Observation.Destination,
            true,
            "MATCH PREVIEW VERIFIED",
            [
                .. events,
                $"EVENT ACT ACTION ATTEMPTS {actTransition.ActionAttempts}",
                $"SELECT STAGE ACTION ATTEMPTS {stageTransition.ActionAttempts}",
            ]);
    }

    private async Task<ObservedStateTransitionActionResult> ClickActAsync(
        OcrTargetRule actRule,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot picker = await _states.RunAsync(
            DebugWorkflowCatalog.EventActPicker,
            device,
            cancellationToken);
        if (!picker.Evaluation.IsMatch)
            return new(false, "EVENT ACT PICKER NOT VERIFIED", [StateLine(picker)]);
        OcrTargetMatch? target = OcrRuleEngine.FindTarget(actRule, picker.Regions);
        if (target is null)
            return new(false, $"{actRule.Name.ToUpperInvariant()} NOT FOUND", [StateLine(picker)]);
        PixelPoint point = target.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return new(true, $"{actRule.Name.ToUpperInvariant()} CLICKED", [$"{actRule.Name.ToUpperInvariant()} [{point.X},{point.Y}] CENTER"]);
    }

    private async Task<ObservedStateTransitionActionResult> ClickSelectStageAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot stage = await _states.RunAsync(
            DebugWorkflowCatalog.EventStagePreview,
            device,
            cancellationToken);
        if (!stage.Evaluation.IsMatch)
            return new(false, "EVENT STAGE PREVIEW NOT VERIFIED", [StateLine(stage)]);
        OcrTargetMatch? select = OcrRuleEngine.FindTarget(
            DebugWorkflowCatalog.EventSelectStageTarget,
            stage.Regions);
        if (select is null)
            return new(false, "SELECT STAGE NOT FOUND", [StateLine(stage)]);
        PixelPoint point = select.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return new(true, "SELECT STAGE CLICKED", [$"SELECT STAGE [{point.X},{point.Y}] CENTER"]);
    }

    private static DebugRunReport TransitionFailed(
        ObservedStateTransitionRunResult transition,
        string label,
        IReadOnlyList<string> events) =>
        new(
            transition.Observation.Result,
            false,
            $"{label} {transition.Observation.Outcome.ToString().ToUpperInvariant()}",
            [
                .. events,
                $"ACTION ATTEMPTS {transition.ActionAttempts}",
                $"INDETERMINATE OBSERVATIONS {transition.IndeterminateObservations}",
                .. transition.LastAction?.Events ?? [],
            ]);
}
