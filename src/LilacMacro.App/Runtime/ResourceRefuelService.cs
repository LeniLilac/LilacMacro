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

        DebugStateSpec stationState = target == ResourceRefuelTarget.GoldMine
            ? DebugWorkflowCatalog.GoldMineRefuel
            : DebugWorkflowCatalog.ResourceDrillRefuel;
        DebugStateSpec confirmationState = target == ResourceRefuelTarget.GoldMine
            ? DebugWorkflowCatalog.GoldMineRefuelConfirmation
            : DebugWorkflowCatalog.ResourceDrillRefuelConfirmation;
        DebugOcrSnapshot station = await OpenStationPanelAsync(
            stationState,
            reservedVirtualKey,
            device,
            cancellationToken).ConfigureAwait(false);
        PixelRect addFuelAnchor = RequiredTarget(station, "Add Fuel").Region.Bounds;
        ObservedStateTransitionRunResult openDialog = await _transitions.RunAsync(
            stationState,
            confirmationState,
            device,
            token => ClickAddFuelAsync(
                stationState,
                device,
                bounds => addFuelAnchor = bounds,
                token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(openDialog, $"{label} Add Fuel dialog");
        status($"{label.ToUpperInvariant()} | ADD FUEL VERIFIED");

        await ConfirmFuelAsync(
            addFuelAnchor,
            stationState,
            confirmationState,
            device,
            status,
            cancellationToken).ConfigureAwait(false);
        status($"{label.ToUpperInvariant()} | REFUEL CONFIRMED");

        status($"{label.ToUpperInvariant()} | RESPAWNING");
        await respawn.RunAsync(
            areasMenuVirtualKey, reservedVirtualKey, device, cancellationToken).ConfigureAwait(false);
        status($"{label.ToUpperInvariant()} | LOBBY VERIFIED");
    }

    private async Task<DebugOcrSnapshot> OpenStationPanelAsync(
        DebugStateSpec stationState,
        int reservedVirtualKey,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot? snapshot = null;
        for (int attempt = 1; attempt <= ResourceRefuelPolicy.StationInteractionAttempts; attempt++)
        {
            await PressAsync('E', 80, reservedVirtualKey, cancellationToken).ConfigureAwait(false);
            await Task.Delay(
                ResourceRefuelPolicy.StationObservationDelay(attempt),
                cancellationToken).ConfigureAwait(false);
            snapshot = await _states.RunAsync(stationState, device, cancellationToken).ConfigureAwait(false);
            if (snapshot.Evaluation.IsMatch) return snapshot;
        }

        throw new InvalidOperationException(
            $"{stationState.Name} Add Fuel control was not verified after " +
            $"{ResourceRefuelPolicy.StationInteractionAttempts} interaction attempt(s).");
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
        Action<PixelRect> observeAnchor,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot station = await _states.RunAsync(stationState, device, cancellationToken)
            .ConfigureAwait(false);
        if (!station.Evaluation.IsMatch)
            return new(false, $"{stationState.Name} NOT VERIFIED", []);
        OcrTargetMatch addFuel = RequiredTarget(station, "Add Fuel");
        observeAnchor(addFuel.Region.Bounds);
        await workspace.ClickRobloxAsync(ClientSize, addFuel.Region.Bounds.Center, cancellationToken)
            .ConfigureAwait(false);
        return new(true, "ADD FUEL CLICKED", ["ADD FUEL VERIFIED + CLICKED"]);
    }

    private async Task ConfirmFuelAsync(
        PixelRect addFuelAnchor,
        DebugStateSpec stationState,
        DebugStateSpec confirmationState,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot dialog = await _states.RunAsync(
            confirmationState, device, cancellationToken).ConfigureAwait(false);
        if (!dialog.Evaluation.IsMatch)
            throw new InvalidOperationException("ADD FUEL DIALOG NOT VERIFIED");
        ResourceRefuelDialogActions actions = DialogActions(addFuelAnchor, dialog);
        status("SELECTING AVAILABLE FUEL");
        await workspace.ClickRobloxAsync(ClientSize, actions.Quantity, cancellationToken).ConfigureAwait(false);
        await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);

        int confirmationAttempts = 0;
        int clearObservations = 0;
        int observationCount = 0;
        while (observationCount < 12)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dialog = await _states.RunAsync(
                confirmationState, device, cancellationToken).ConfigureAwait(false);
            observationCount++;
            if (dialog.Evaluation.IsMatch)
            {
                clearObservations = 0;
                if (confirmationAttempts >= ResourceRefuelPolicy.ConfirmationAttempts)
                {
                    throw new InvalidOperationException(
                        "The Add Fuel confirmation remained visible after three clicks.");
                }

                actions = DialogActions(addFuelAnchor, dialog);
                await workspace.ClickRobloxAsync(
                    ClientSize, actions.Confirm, cancellationToken).ConfigureAwait(false);
                confirmationAttempts++;
                await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            DebugOcrSnapshot station = await _states.RunAsync(
                stationState, device, cancellationToken).ConfigureAwait(false);
            clearObservations = station.Evaluation.IsMatch ? clearObservations + 1 : 0;
            if (clearObservations >= 2) return;
            await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "The Add Fuel confirmation did not reach a stable cleared state.");
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

    private static ResourceRefuelDialogActions DialogActions(
        PixelRect addFuelAnchor,
        DebugOcrSnapshot dialog)
    {
        PixelRect confirm = RequiredTarget(dialog, "Confirm").Region.Bounds;
        PixelRect cancel = RequiredTarget(dialog, "Cancel").Region.Bounds;
        return ResourceRefuelPolicy.TryResolveDialogActions(
            addFuelAnchor,
            confirm,
            cancel,
            ClientSize,
            out ResourceRefuelDialogActions actions)
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
