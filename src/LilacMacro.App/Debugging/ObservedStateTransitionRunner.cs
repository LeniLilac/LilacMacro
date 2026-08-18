using System.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Debugging;

internal sealed record ObservedStateTransitionRunResult(
    bool Succeeded,
    DebugStateTransitionObservation Observation,
    int ActionAttempts,
    int IndeterminateObservations,
    ObservedStateTransitionActionResult? LastAction);

internal sealed record ObservedStateTransitionActionResult(
    bool Succeeded,
    string Status,
    IReadOnlyList<string> Events)
{
    public static ObservedStateTransitionActionResult From(DebugRunReport report) =>
        new(report.Succeeded, report.Status, report.Events);
}

internal sealed class ObservedStateTransitionRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan PostActionDelay = TimeSpan.FromMilliseconds(350);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<ObservedStateTransitionRunResult> RunAsync(
        DebugStateSpec source,
        DebugStateSpec destination,
        string device,
        Func<CancellationToken, Task<ObservedStateTransitionActionResult>> sourceAction,
        CancellationToken cancellationToken,
        ObservedStateTransitionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sourceAction);
        budget ??= new ObservedStateTransitionBudget();
        budget.Validate();

        int actionAttempts = 0;
        int indeterminateObservations = 0;
        ObservedStateTransitionActionResult? lastAction = null;
        Stopwatch? retryWindow = budget.RetryWindow is not null
            ? Stopwatch.StartNew()
            : null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DebugStateTransitionObservation observation = await _states.ObserveTransitionAsync(
                source,
                destination,
                device,
                cancellationToken);
            ObservedStateTransitionDecision decision = ObservedStateTransitionPolicy.Decide(
                observation.Outcome,
                actionAttempts,
                indeterminateObservations,
                budget,
                retryWindow is not null &&
                budget.RetryWindow is TimeSpan retryWindowLimit &&
                retryWindow.Elapsed >= retryWindowLimit);
            switch (decision)
            {
                case ObservedStateTransitionDecision.Complete:
                    return new ObservedStateTransitionRunResult(
                        true, observation, actionAttempts, indeterminateObservations, lastAction);
                case ObservedStateTransitionDecision.Exhausted:
                    return new ObservedStateTransitionRunResult(
                        false, observation, actionAttempts, indeterminateObservations, lastAction);
                case ObservedStateTransitionDecision.RetrySourceAction:
                    lastAction = await sourceAction(cancellationToken);
                    actionAttempts++;
                    if (lastAction.Succeeded)
                    {
                        indeterminateObservations = 0;
                        await Task.Delay(
                            BoundedDelay(PostActionDelay, budget, retryWindow),
                            cancellationToken);
                    }
                    else
                    {
                        indeterminateObservations++;
                        await Task.Delay(
                            RetryDelay(indeterminateObservations - 1, budget, retryWindow),
                            cancellationToken);
                    }
                    break;
                case ObservedStateTransitionDecision.ObserveAgain:
                    indeterminateObservations++;
                    await Task.Delay(
                        RetryDelay(indeterminateObservations - 1, budget, retryWindow),
                        cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(decision));
            }
        }
    }

    private static TimeSpan RetryDelay(
        int completedIndeterminateObservations,
        ObservedStateTransitionBudget budget,
        Stopwatch? retryWindow)
    {
        if (retryWindow is null || budget.RetryWindow is not TimeSpan window)
            return ObservedStateTransitionPolicy.ObservationDelay(
                completedIndeterminateObservations,
                budget);

        TimeSpan remaining = window - retryWindow.Elapsed;
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;
        TimeSpan interval = TimeSpan.FromMilliseconds(
            budget.RetryIntervalMilliseconds ??
            ObservedStateTransitionBudget.DefaultInitialObservationDelayMilliseconds);
        return interval < remaining ? interval : remaining;
    }

    private static TimeSpan BoundedDelay(
        TimeSpan requested,
        ObservedStateTransitionBudget budget,
        Stopwatch? retryWindow)
    {
        if (retryWindow is null || budget.RetryWindow is not TimeSpan window)
            return requested;

        TimeSpan remaining = window - retryWindow.Elapsed;
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;
        return remaining < requested ? remaining : requested;
    }
}
