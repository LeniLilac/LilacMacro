using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class MatchTerminalService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly DebugResultRunner _results = new(workspace, ocr);
    private readonly ObservedStateTransitionRunner _transitions = new(workspace, ocr);

    public async Task<MatchTerminalOutcome> WaitAsync(
        string device,
        TimeSpan timeout,
        bool dismissRaidDrops,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                DebugOcrSnapshot snapshot = await _states.RunAsync(
                    DebugWorkflowCatalog.Victory, device, deadline.Token);
                if (snapshot.Evaluation.IsMatch)
                {
                    status?.Invoke("VICTORY VERIFIED");
                    return MatchTerminalOutcome.Victory;
                }
                if (DebugOcrStateRunner.Evaluate(DebugWorkflowCatalog.Defeat, snapshot.Regions).IsMatch)
                {
                    status?.Invoke("DEFEAT VERIFIED");
                    return MatchTerminalOutcome.Defeat;
                }
                if (dismissRaidDrops)
                {
                    await workspace.ClickRobloxAsync(
                        DebugWorkflowCatalog.ClientSize,
                        RaidDropDismissalPolicy.ActionPoint,
                        deadline.Token);
                    status?.Invoke("RAID DROP DISMISSAL CLICK");
                }
                status?.Invoke("WAITING FOR VICTORY / DEFEAT");
                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Victory or Defeat was not verified within {timeout.TotalMinutes:N0} minutes.");
        }
    }

    public async Task<MatchTerminalOutcome?> TryObserveAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.Victory, device, cancellationToken).ConfigureAwait(false);
        if (snapshot.Evaluation.IsMatch) return MatchTerminalOutcome.Victory;
        return DebugOcrStateRunner.Evaluate(DebugWorkflowCatalog.Defeat, snapshot.Regions).IsMatch
            ? MatchTerminalOutcome.Defeat
            : null;
    }

    public async Task<MatchTerminalOutcome> WaitUntilTerminalAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                MatchTerminalOutcome? outcome = await TryObserveAsync(
                    device, cancellationToken).ConfigureAwait(false);
                if (outcome is MatchTerminalOutcome terminal)
                {
                    status?.Invoke($"{terminal.ToString().ToUpperInvariant()} VERIFIED");
                    return terminal;
                }
            }
            catch (Exception error) when (
                !cancellationToken.IsCancellationRequested && IsRecoverableObservationFailure(error))
            {
                status?.Invoke($"TERMINAL OBSERVATION RETRY | {error.Message}");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RepeatAsync(
        MatchTerminalOutcome outcome,
        string device,
        CancellationToken cancellationToken)
    {
        DebugStateSpec state = outcome == MatchTerminalOutcome.Victory
            ? DebugWorkflowCatalog.Victory
            : DebugWorkflowCatalog.Defeat;
        ObservedStateTransitionRunResult transition = await _transitions.RunAsync(
            state,
            DebugWorkflowCatalog.MatchPrestart,
            device,
            async token => ObservedStateTransitionActionResult.From(
                await _results.RepeatAsync(state, device, token).ConfigureAwait(false)),
            cancellationToken,
            MatchLoadPolicy.TransitionBudget).ConfigureAwait(false);
        if (!transition.Succeeded)
        {
            throw new InvalidOperationException(
                $"Repeat Stage did not reach Match Prestart after {transition.ActionAttempts} action attempt(s) " +
                $"({transition.Observation.Outcome}).");
        }
    }

    private static bool IsRecoverableObservationFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or
        InvalidOperationException or TimeoutException;
}
