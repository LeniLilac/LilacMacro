using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Runtime;

internal sealed class ResourceRefuelService(
    WorkspaceController workspace,
    OcrRunner ocr,
    UtilityRespawnService respawn)
{
    private static readonly TimeSpan ObservationDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan HubTeleportDelay = TimeSpan.FromMilliseconds(5500);
    private static readonly TimeSpan RouteStepDelay = TimeSpan.FromMilliseconds(120);
    private static readonly PixelSize ClientSize = DebugWorkflowCatalog.ClientSize;
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly ObservedStateTransitionRunner _transitions = new(workspace, ocr);

    public async Task RunRouteAsync(
        string route,
        int? areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(status);
        if (areasMenuVirtualKey is null)
            throw new InvalidDataException("Areas menu must have a key for refuel tasks.");

        foreach (ResourceRefuelTarget target in ResourceRefuelPolicy.TargetsFor(route))
        {
            await RefuelAsync(
                target,
                areasMenuVirtualKey.Value,
                reservedVirtualKey,
                device,
                status,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefuelAsync(
        ResourceRefuelTarget target,
        int areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        string label = Label(target);
        status($"{label.ToUpperInvariant()} | OPENING EXPEDITION HUB");
        await OpenExpeditionHubAsync(
            areasMenuVirtualKey,
            reservedVirtualKey,
            device,
            cancellationToken).ConfigureAwait(false);

        status($"{label.ToUpperInvariant()} | WALKING VERIFIED ROUTE");
        foreach (ResourceRefuelWalkStep step in ResourceRefuelPolicy.WalkFor(target))
        {
            await PressAsync(step.VirtualKey, step.HoldMilliseconds, reservedVirtualKey, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(RouteStepDelay, cancellationToken).ConfigureAwait(false);
        }
        await PressAsync('E', 80, reservedVirtualKey, cancellationToken).ConfigureAwait(false);

        DebugStateSpec stationState = target == ResourceRefuelTarget.GoldMine
            ? DebugWorkflowCatalog.GoldMineRefuel
            : DebugWorkflowCatalog.ResourceDrillRefuel;
        ObservedStateTransitionRunResult openDialog = await _transitions.RunAsync(
            stationState,
            DebugWorkflowCatalog.AddFuelDialog,
            device,
            token => ClickAddFuelAsync(stationState, device, token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(openDialog, $"{label} Add Fuel dialog");
        status($"{label.ToUpperInvariant()} | ADD FUEL VERIFIED");

        ObservedStateTransitionRunResult confirm = await _transitions.RunAsync(
            DebugWorkflowCatalog.AddFuelDialog,
            stationState,
            device,
            token => ConfirmFuelAsync(device, status, token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(confirm, $"{label} refuel confirmation");
        status($"{label.ToUpperInvariant()} | REFUEL CONFIRMED");

        status($"{label.ToUpperInvariant()} | RESPAWNING");
        await respawn.RunAsync(
            areasMenuVirtualKey, reservedVirtualKey, device, cancellationToken).ConfigureAwait(false);
        status($"{label.ToUpperInvariant()} | LOBBY VERIFIED");
    }

    private async Task OpenExpeditionHubAsync(
        int areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        CancellationToken cancellationToken)
    {
        ObservedStateTransitionRunResult openAreas = await _transitions.RunAsync(
            DebugWorkflowCatalog.Lobby,
            DebugWorkflowCatalog.AreasUi,
            device,
            token => PressActionAsync(areasMenuVirtualKey, reservedVirtualKey, token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(openAreas, "Lobby to Areas");

        ObservedStateTransitionRunResult openExpedition = await _transitions.RunAsync(
            DebugWorkflowCatalog.AreasUi,
            DebugWorkflowCatalog.ExpeditionHub,
            device,
            token => ClickExpeditionAreaAsync(device, token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(openExpedition, "Areas to Expedition Hub");

        DebugOcrSnapshot hub = openExpedition.Observation.Destination;
        OcrTargetMatch hubTarget = RequiredTarget(hub, "Expedition Hub");
        await workspace.ClickRobloxAsync(ClientSize, hubTarget.Region.Bounds.Center, cancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(HubTeleportDelay, cancellationToken).ConfigureAwait(false);
        await workspace.FocusRobloxAsync(ClientSize, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ObservedStateTransitionActionResult> ClickAddFuelAsync(
        DebugStateSpec stationState,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot station = await _states.RunAsync(stationState, device, cancellationToken)
            .ConfigureAwait(false);
        if (!station.Evaluation.IsMatch)
            return new(false, $"{stationState.Name} NOT VERIFIED", []);
        OcrTargetMatch addFuel = RequiredTarget(station, "Add Fuel");
        await workspace.ClickRobloxAsync(ClientSize, addFuel.Region.Bounds.Center, cancellationToken)
            .ConfigureAwait(false);
        return new(true, "ADD FUEL CLICKED", ["ADD FUEL VERIFIED + CLICKED"]);
    }

    private async Task<ObservedStateTransitionActionResult> ConfirmFuelAsync(
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot dialog = await _states.RunAsync(
            DebugWorkflowCatalog.AddFuelDialog, device, cancellationToken).ConfigureAwait(false);
        if (!dialog.Evaluation.IsMatch)
            return new(false, "ADD FUEL DIALOG NOT VERIFIED", []);
        ResourceRefuelDialogActions actions = DialogActions(dialog);
        status("SELECTING AVAILABLE FUEL");
        await workspace.ClickRobloxAsync(ClientSize, actions.Quantity, cancellationToken).ConfigureAwait(false);
        await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        dialog = await _states.RunAsync(
            DebugWorkflowCatalog.AddFuelDialog, device, cancellationToken).ConfigureAwait(false);
        if (!dialog.Evaluation.IsMatch)
            return new(false, "ADD FUEL DIALOG CHANGED BEFORE CONFIRM", []);
        actions = DialogActions(dialog);
        await workspace.ClickRobloxAsync(ClientSize, actions.Confirm, cancellationToken).ConfigureAwait(false);
        return new(true, "REFUEL CONFIRM CLICKED", ["QUANTITY + CONFIRM CLICKED"]);
    }

    private Task<ObservedStateTransitionActionResult> PressActionAsync(
        int virtualKey,
        int reservedVirtualKey,
        CancellationToken cancellationToken) => PressActionCoreAsync(
            virtualKey, reservedVirtualKey, cancellationToken);

    private async Task<ObservedStateTransitionActionResult> PressActionCoreAsync(
        int virtualKey,
        int reservedVirtualKey,
        CancellationToken cancellationToken)
    {
        await PressAsync(virtualKey, 80, reservedVirtualKey, cancellationToken).ConfigureAwait(false);
        return new(true, "NAVIGATION KEY SENT", ["NAVIGATION KEY SENT"]);
    }

    private async Task<ObservedStateTransitionActionResult> ClickExpeditionAreaAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot areas = await _states.RunAsync(
            DebugWorkflowCatalog.AreasUi, device, cancellationToken).ConfigureAwait(false);
        if (!areas.Evaluation.IsMatch) return new(false, "AREAS UI NOT VERIFIED", []);
        OcrTargetMatch expedition = AreaSelectionRules.Find(AreaCategory.Expedition, areas.Regions)
            ?? throw new InvalidOperationException("Verified Areas UI did not expose Expedition.");
        await workspace.ClickRobloxAsync(ClientSize, expedition.Region.Bounds.Center, cancellationToken)
            .ConfigureAwait(false);
        return new(true, "EXPEDITION AREA CLICKED", ["EXPEDITION AREA VERIFIED + CLICKED"]);
    }

    private static void RequireTransition(ObservedStateTransitionRunResult result, string name)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"{name} transition failed after {result.ActionAttempts} action attempt(s) " +
                $"({result.Observation.Outcome}).");
    }

    private Task PressAsync(
        int virtualKey,
        int holdMilliseconds,
        int reservedVirtualKey,
        CancellationToken cancellationToken) => workspace.RunKeySequenceAsync(
        ClientSize,
        AutomationKeySequence.Create(
        [
            AutomationKeyPress.Create(virtualKey, holdMilliseconds, reservedVirtualKey),
        ]),
        cancellationToken);

    private async Task<DebugOcrSnapshot> RequireStateAsync(
        DebugStateSpec state,
        string device,
        int maximumObservations,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.WaitForMatchAsync(
            state,
            device,
            maximumObservations,
            ObservationDelay,
            cancellationToken).ConfigureAwait(false);
        return snapshot.Evaluation.IsMatch
            ? snapshot
            : throw new InvalidOperationException($"{state.Name} was not verified before its deadline.");
    }

    private static ResourceRefuelDialogActions DialogActions(DebugOcrSnapshot dialog)
    {
        PixelRect confirm = RequiredTarget(dialog, "Confirm").Region.Bounds;
        PixelRect cancel = RequiredTarget(dialog, "Cancel").Region.Bounds;
        return ResourceRefuelPolicy.TryResolveDialogActions(confirm, cancel, ClientSize, out ResourceRefuelDialogActions actions)
            ? actions
            : throw new InvalidOperationException("The live refuel dialog layout was not safe to use.");
    }

    private static OcrTargetMatch RequiredTarget(DebugOcrSnapshot snapshot, string name) =>
        snapshot.Evaluation.Matches.FirstOrDefault(match =>
            string.Equals(match.Target, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Verified {snapshot.State} did not expose {name}.");

    private static string Label(ResourceRefuelTarget target) => target switch
    {
        ResourceRefuelTarget.GoldMine => "Gold Mine",
        ResourceRefuelTarget.ResourceDrill => "Resource Drill",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
