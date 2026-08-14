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
                budget);
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
                        await Task.Delay(PostActionDelay, cancellationToken);
                    }
                    else
                    {
                        indeterminateObservations++;
                        await Task.Delay(
                            ObservedStateTransitionPolicy.ObservationDelay(
                                indeterminateObservations - 1,
                                budget),
                            cancellationToken);
                    }
                    break;
                case ObservedStateTransitionDecision.ObserveAgain:
                    indeterminateObservations++;
                    await Task.Delay(
                        ObservedStateTransitionPolicy.ObservationDelay(
                            indeterminateObservations - 1,
                            budget),
                        cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(decision));
            }
        }
    }
}
