using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Debugging;

internal sealed class WireTransitionService(
    WorkspaceController workspace,
    OcrRunner ocr,
    DeepDebugSessionService deepDebug)
{
    private readonly ObservedStateTransitionRunner _transitions = new(workspace, ocr);

    public Task<bool> RunAsync(
        StoryWireStage stage,
        DebugStateSpec source,
        DebugStateSpec destination,
        string device,
        Func<CancellationToken, Task<DebugRunReport>> sourceAction,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        RunAsync(
            stage,
            source,
            destination,
            device,
            async token => ObservedStateTransitionActionResult.From(await sourceAction(token)),
            progress,
            cancellationToken);

    public async Task<bool> RunAsync(
        StoryWireStage stage,
        DebugStateSpec source,
        DebugStateSpec destination,
        string device,
        Func<CancellationToken, Task<ObservedStateTransitionActionResult>> sourceAction,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new StoryWireProgress(
            stage, StoryWireStageStatus.Running, $"{source.Name} -> {destination.Name}", []));
        deepDebug.RecordEvent("wire", "transition_started", new
        {
            Stage = StoryWireTestRunner.Format(stage),
            Source = source.Name,
            Destination = destination.Name,
        });
        ObservedStateTransitionRunResult result = await _transitions.RunAsync(
            source, destination, device, sourceAction, cancellationToken);
        string status = result.Succeeded
            ? $"{destination.Name.ToUpperInvariant()} VERIFIED"
            : result.Observation.Outcome == ObservedStateTransitionOutcome.SourceRetained
                ? $"{source.Name.ToUpperInvariant()} RETAINED AFTER {result.ActionAttempts} ATTEMPTS"
                : $"{source.Name.ToUpperInvariant()} -> {destination.Name.ToUpperInvariant()} INDETERMINATE";
        string[] events =
        [
            $"TARGET-FIRST {destination.Name.ToUpperInvariant()}",
            $"SOURCE-FALLBACK {source.Name.ToUpperInvariant()}",
            $"ACTION ATTEMPTS {result.ActionAttempts}",
            $"INDETERMINATE OBSERVATIONS {result.IndeterminateObservations}",
            .. result.LastAction?.Events ?? [],
        ];
        deepDebug.RecordEvent("wire", "transition_completed", new
        {
            Stage = StoryWireTestRunner.Format(stage),
            Source = source.Name,
            Destination = destination.Name,
            result.Succeeded,
            Outcome = result.Observation.Outcome.ToString(),
            result.ActionAttempts,
            result.IndeterminateObservations,
            Status = status,
        });
        progress.Report(new StoryWireProgress(
            stage,
            result.Succeeded ? StoryWireStageStatus.Passed : StoryWireStageStatus.Failed,
            status,
            events));
        return result.Succeeded;
    }
}
