using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using static LilacMacro.App.Debugging.DebugReportFactory;

namespace LilacMacro.App.Debugging;

internal sealed class DebugLobbyRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan NavigationDelay = TimeSpan.FromSeconds(5);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public Task<DebugRunReport> CheckLobbyAsync(
        string device,
        CancellationToken cancellationToken) =>
        CheckStateAsync(DebugWorkflowCatalog.Lobby, device, cancellationToken);

    public Task<DebugRunReport> OpenPlayAsync(
        string device,
        CancellationToken cancellationToken) =>
        OpenFromLobbyAsync("Play", DebugWorkflowCatalog.PlayUi, device, cancellationToken);

    public Task<DebugRunReport> OpenUnitsAsync(
        string device,
        CancellationToken cancellationToken) =>
        OpenFromLobbyAsync("Units", DebugWorkflowCatalog.UnitInventory, device, cancellationToken);

    public Task<DebugRunReport> OpenEventsAsync(
        string device,
        CancellationToken cancellationToken) =>
        OpenFromLobbyAsync("Events", DebugWorkflowCatalog.EventSelect, device, cancellationToken);

    public Task<DebugRunReport> OpenAreasAsync(
        string device,
        CancellationToken cancellationToken) =>
        OpenFromLobbyAsync("Areas", DebugWorkflowCatalog.AreasUi, device, cancellationToken);

    public async Task<DebugRunReport> CloseUnitsViaButtonAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot source = await _states.RunAsync(
            DebugWorkflowCatalog.TeamSwap,
            device,
            cancellationToken);
        if (!source.Evaluation.IsMatch) return FailedState(source);

        DebugOcrSnapshot menu = await _states.RunAsync(
            DebugWorkflowCatalog.Lobby,
            device,
            cancellationToken);
        OcrTargetMatch? target = menu.Evaluation.Matches.FirstOrDefault(
            match => match.Target.Equals("Units", StringComparison.Ordinal));
        if (target is null) return MissingTarget(menu, "UNITS");

        PixelPoint point = target.Region.Bounds.TopCenter;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        await Task.Delay(NavigationDelay, cancellationToken);
        DebugStateTransitionObservation transition = await _states.ObserveTransitionAsync(
            DebugWorkflowCatalog.TeamSwap,
            DebugWorkflowCatalog.Lobby,
            device,
            cancellationToken);
        bool succeeded = transition.Outcome == ObservedStateTransitionOutcome.DestinationReached;
        return new DebugRunReport(
            transition.Result,
            succeeded,
            succeeded
                ? "LOBBY TRUE"
                : transition.Outcome == ObservedStateTransitionOutcome.SourceRetained
                    ? "TEAM SWAP RETAINED"
                    : "UNITS CLOSE INDETERMINATE",
            [
                "TEAM SWAP VERIFIED",
                $"UNITS [{point.X},{point.Y}] TOP",
                "WAIT 5000 MS",
                StateLine(transition.Destination),
                .. (transition.Source is null ? [] : new[] { StateLine(transition.Source) }),
            ]);
    }

    public Task<DebugRunReport> CheckEventsAsync(
        string device,
        CancellationToken cancellationToken) =>
        CheckStateAsync(DebugWorkflowCatalog.EventSelect, device, cancellationToken);

    public async Task<DebugRunReport> SelectEventAsync(
        EventDestination destination,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.EventSelect,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetRule rule = EventSelectionRules.TargetFor(destination);
        OcrTargetMatch? target = EventSelectionRules.Find(destination, snapshot.Regions);
        if (target is null) return MissingTarget(snapshot, rule.Name.ToUpperInvariant());

        PixelPoint point = target.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return ClickReport(snapshot, target, point, "CENTER");
    }

    public Task<DebugRunReport> CheckAreasAsync(
        string device,
        CancellationToken cancellationToken) =>
        CheckStateAsync(DebugWorkflowCatalog.AreasUi, device, cancellationToken);

    public async Task<DebugRunReport> SelectAreaAsync(
        AreaCategory category,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.AreasUi,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetRule rule = AreaSelectionRules.TargetFor(category);
        OcrTargetMatch? target = AreaSelectionRules.Find(category, snapshot.Regions);
        if (target is null) return MissingTarget(snapshot, rule.Name.ToUpperInvariant());

        PixelPoint point = target.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return ClickReport(snapshot, target, point, "LEFTMOST CENTER");
    }

    public Task<DebugRunReport> CheckPlayUiAsync(
        string device,
        CancellationToken cancellationToken) =>
        CheckStateAsync(DebugWorkflowCatalog.PlayUi, device, cancellationToken);

    public async Task<DebugRunReport> SelectModeAsync(
        string mode,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.PlayUi,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetMatch? target = snapshot.Evaluation.Matches.FirstOrDefault(
            match => match.Target.Equals(mode, StringComparison.Ordinal));
        if (target is null) return MissingTarget(snapshot, mode.ToUpperInvariant());

        PixelPoint point = target.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return ClickReport(snapshot, target, point, "CENTER");
    }

    private async Task<DebugRunReport> OpenFromLobbyAsync(
        string targetName,
        DebugStateSpec destinationState,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot lobby = await _states.RunAsync(
            DebugWorkflowCatalog.Lobby,
            device,
            cancellationToken);
        if (!lobby.Evaluation.IsMatch) return FailedState(lobby);

        OcrTargetMatch? target = lobby.Evaluation.Matches.FirstOrDefault(
            match => match.Target.Equals(targetName, StringComparison.Ordinal));
        if (target is null) return MissingTarget(lobby, targetName.ToUpperInvariant());

        PixelPoint point = target.Region.Bounds.TopCenter;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        await Task.Delay(NavigationDelay, cancellationToken);

        DebugStateTransitionObservation transition = await _states.ObserveTransitionAsync(
            DebugWorkflowCatalog.Lobby,
            destinationState,
            device,
            cancellationToken);
        bool succeeded = transition.Outcome == ObservedStateTransitionOutcome.DestinationReached;
        return new DebugRunReport(
            transition.Result,
            succeeded,
            succeeded
                ? $"{transition.Destination.State} TRUE"
                : transition.Outcome == ObservedStateTransitionOutcome.SourceRetained
                    ? "LOBBY RETAINED"
                    : $"{destinationState.Name} INDETERMINATE",
            [
                $"{targetName.ToUpperInvariant()} [{point.X},{point.Y}] TOP",
                "WAIT 5000 MS",
                StateLine(transition.Destination),
                .. (transition.Source is null ? [] : new[] { StateLine(transition.Source) }),
            ]);
    }

    private async Task<DebugRunReport> CheckStateAsync(
        DebugStateSpec state,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(state, device, cancellationToken);
        return StateReport(snapshot);
    }
}
