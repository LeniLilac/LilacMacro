using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Runtime;

internal enum CheckpointTransitionDecision
{
    ObserveAgain,
    OpenConfirmation,
    Confirm,
    Complete,
}

internal static class CheckpointTransitionPolicy
{
    internal const int MaximumActions = 4;
    internal const int MaximumIndeterminateObservations = 12;

    public static bool CanAct(int actionAttempts) => actionAttempts < MaximumActions;

    public static bool CanObserve(int indeterminateObservations) =>
        indeterminateObservations < MaximumIndeterminateObservations;

    public static CheckpointTransitionDecision Decide(
        bool confirmationObserved,
        bool sourceObserved,
        bool transitionStarted,
        int consecutiveSourceObservations,
        int consecutiveClearObservations)
    {
        if (confirmationObserved) return CheckpointTransitionDecision.Confirm;
        if (sourceObserved)
        {
            return !transitionStarted || consecutiveSourceObservations >= 2
                ? CheckpointTransitionDecision.OpenConfirmation
                : CheckpointTransitionDecision.ObserveAgain;
        }

        return transitionStarted && consecutiveClearObservations >= 2
            ? CheckpointTransitionDecision.Complete
            : CheckpointTransitionDecision.ObserveAgain;
    }
}

internal sealed class ExpeditionCheckpointService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan ObservationDelay = TimeSpan.FromMilliseconds(300);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly ExpeditionRewardPopupService _rewardPopup = new(workspace, ocr);

    public Task ContinueAsync(string device, Action<string>? status, CancellationToken cancellationToken) =>
        RunAsync(
            "CHECKPOINT",
            "Continue",
            ExpeditionCheckpointStateCatalog.SpawnContinueSource,
            ExpeditionCheckpointStateCatalog.ContinueConfirmation,
            waitForArrival: false,
            device,
            status,
            cancellationToken);

    public Task ContinueAfterArrivalAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken) =>
        RunAsync(
            "CHECKPOINT",
            "Continue",
            ExpeditionCheckpointStateCatalog.ContinueSource,
            ExpeditionCheckpointStateCatalog.ContinueConfirmation,
            waitForArrival: true,
            device,
            status,
            cancellationToken);

    public Task ContinueEncounterAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken) =>
        RunAsync(
            "ENCOUNTER",
            "Continue",
            ExpeditionCheckpointStateCatalog.EncounterContinueSource,
            ExpeditionCheckpointStateCatalog.EncounterContinueConfirmation,
            waitForArrival: true,
            device,
            status,
            cancellationToken);

    public Task ExtractAsync(string device, Action<string>? status, CancellationToken cancellationToken) =>
        RunAsync(
            "CHECKPOINT",
            "Extract",
            ExpeditionCheckpointStateCatalog.ExtractSource,
            ExpeditionCheckpointStateCatalog.ExtractConfirmation,
            waitForArrival: true,
            device,
            status,
            cancellationToken);

    public async Task<ExpeditionLiveControl> ObserveLiveControlAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        bool checkpointAvailable = await IsSourceAvailableAsync(
            ExpeditionCheckpointStateCatalog.ContinueSource,
            "CHECKPOINT CONTROLS VERIFIED",
            device,
            status,
            cancellationToken).ConfigureAwait(false);
        if (checkpointAvailable) return ExpeditionLiveControl.Checkpoint;

        bool encounterAvailable = await IsSourceAvailableAsync(
            ExpeditionCheckpointStateCatalog.EncounterContinueSource,
            "ENCOUNTER CONTROLS VERIFIED",
            device,
            status,
            cancellationToken).ConfigureAwait(false);
        return ExpeditionLiveControlPolicy.Select(
            checkpointAvailable: false,
            encounterAvailable);
    }

    private async Task<bool> IsSourceAvailableAsync(
        DebugStateSpec sourceState,
        string verifiedStatus,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot source = await _states.RunAsync(
            sourceState, device, cancellationToken).ConfigureAwait(false);
        if (!source.Evaluation.IsMatch) return false;

        status?.Invoke(verifiedStatus);
        return true;
    }

    private async Task RunAsync(
        string workflowName,
        string actionName,
        DebugStateSpec sourceState,
        DebugStateSpec confirmationState,
        bool waitForArrival,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (waitForArrival)
        {
            await WaitForArrivalAsync(
                workflowName, sourceState, confirmationState, device, status, cancellationToken)
                .ConfigureAwait(false);
        }

        int actionAttempts = 0;
        int indeterminateObservations = 0;
        int consecutiveSourceObservations = 0;
        int consecutiveClearObservations = 0;
        bool transitionStarted = false;

        while (CheckpointTransitionPolicy.CanObserve(indeterminateObservations))
        {
            if (await _rewardPopup.DismissAllAsync(
                    device, status, cancellationToken).ConfigureAwait(false))
            {
                indeterminateObservations = 0;
                consecutiveSourceObservations = 0;
                consecutiveClearObservations = 0;
                continue;
            }

            DebugOcrSnapshot confirmation = await _states.RunAsync(
                confirmationState, device, cancellationToken).ConfigureAwait(false);
            DebugOcrSnapshot? source = null;
            if (!confirmation.Evaluation.IsMatch)
            {
                source = await _states.RunAsync(sourceState, device, cancellationToken)
                    .ConfigureAwait(false);
            }

            consecutiveSourceObservations = source?.Evaluation.IsMatch == true
                ? consecutiveSourceObservations + 1
                : 0;
            consecutiveClearObservations = !confirmation.Evaluation.IsMatch &&
                                           source?.Evaluation.IsMatch != true
                ? consecutiveClearObservations + 1
                : 0;
            CheckpointTransitionDecision decision = CheckpointTransitionPolicy.Decide(
                confirmation.Evaluation.IsMatch,
                source?.Evaluation.IsMatch == true,
                transitionStarted,
                consecutiveSourceObservations,
                consecutiveClearObservations);

            if (decision is (CheckpointTransitionDecision.Confirm or
                CheckpointTransitionDecision.OpenConfirmation) &&
                await _rewardPopup.DismissAllAsync(
                    device, status, cancellationToken).ConfigureAwait(false))
            {
                indeterminateObservations = 0;
                consecutiveSourceObservations = 0;
                consecutiveClearObservations = 0;
                continue;
            }

            if (decision is CheckpointTransitionDecision.Confirm or
                CheckpointTransitionDecision.OpenConfirmation &&
                !CheckpointTransitionPolicy.CanAct(actionAttempts))
            {
                indeterminateObservations++;
                status?.Invoke(
                    $"{workflowName} {actionName.ToUpperInvariant()} ACTION LIMIT; VERIFYING");
                await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            switch (decision)
            {
                case CheckpointTransitionDecision.Confirm:
                    await ClickTargetAsync(confirmation, actionName, cancellationToken)
                        .ConfigureAwait(false);
                    actionAttempts++;
                    transitionStarted = true;
                    indeterminateObservations = 0;
                    consecutiveSourceObservations = 0;
                    consecutiveClearObservations = 0;
                    status?.Invoke($"{workflowName} {actionName.ToUpperInvariant()} CONFIRM {actionAttempts}/{CheckpointTransitionPolicy.MaximumActions}");
                    break;
                case CheckpointTransitionDecision.OpenConfirmation:
                    await ClickTargetAsync(source!, actionName, cancellationToken).ConfigureAwait(false);
                    actionAttempts++;
                    transitionStarted = true;
                    indeterminateObservations = 0;
                    consecutiveSourceObservations = 0;
                    consecutiveClearObservations = 0;
                    status?.Invoke($"{workflowName} {actionName.ToUpperInvariant()} CLICK {actionAttempts}/{CheckpointTransitionPolicy.MaximumActions}");
                    break;
                case CheckpointTransitionDecision.Complete:
                    status?.Invoke($"{workflowName} {actionName.ToUpperInvariant()} CONFIRMED");
                    return;
                case CheckpointTransitionDecision.ObserveAgain:
                    indeterminateObservations++;
                    if (transitionStarted && source?.Evaluation.IsMatch == true)
                    {
                        status?.Invoke($"{workflowName} {actionName.ToUpperInvariant()} SOURCE RETAINED");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(decision));
            }

            await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"{workflowName} {actionName.ToLowerInvariant()} transition was not verified after " +
            $"{actionAttempts} action attempt(s) and {indeterminateObservations} indeterminate observation(s).");
    }

    private async Task WaitForArrivalAsync(
        string workflowName,
        DebugStateSpec sourceState,
        DebugStateSpec confirmationState,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        status?.Invoke($"WAITING FOR {workflowName} ARRIVAL CONTINUE");
        for (int observation = 1;
             observation <= ExpeditionNodeArrivalPolicy.MaximumObservations;
             observation++)
        {
            if (await _rewardPopup.DismissAllAsync(
                    device, status, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            DebugOcrSnapshot confirmation = await _states.RunAsync(
                confirmationState, device, cancellationToken).ConfigureAwait(false);
            if (confirmation.Evaluation.IsMatch)
            {
                status?.Invoke($"{workflowName} ARRIVAL CONFIRMATION ALREADY OPEN");
                return;
            }

            DebugOcrSnapshot source = await _states.RunAsync(
                sourceState, device, cancellationToken).ConfigureAwait(false);
            if (source.Evaluation.IsMatch)
            {
                status?.Invoke($"{workflowName} ARRIVAL CONTINUE VERIFIED");
                return;
            }

            if (observation < ExpeditionNodeArrivalPolicy.MaximumObservations)
            {
                await Task.Delay(
                    ExpeditionNodeArrivalPolicy.RetryMilliseconds,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"{workflowName} node did not expose its Continue control after ship arrival.");
    }

    private Task ClickTargetAsync(
        DebugOcrSnapshot snapshot,
        string targetName,
        CancellationToken cancellationToken)
    {
        OcrTargetMatch target = snapshot.Evaluation.Matches.FirstOrDefault(match =>
            string.Equals(match.Target, targetName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Verified {snapshot.State} did not expose {targetName}.");
        return workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            target.Region.Bounds.Center,
            cancellationToken);
    }
}
