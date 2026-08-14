using LilacMacro.App.Diagnostics;
using LilacMacro.App.Workspace;

namespace LilacMacro.App.Debugging;

internal sealed class WireStateService(
    WorkspaceController workspace,
    DeepDebugSessionService deepDebug)
{
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(400);
    private readonly WireHybridEvidenceService _hybrid = new(workspace, deepDebug);

    public async Task<bool> ActAsync(
        StoryWireStage stage,
        Func<CancellationToken, Task<DebugRunReport>> action,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new StoryWireProgress(stage, StoryWireStageStatus.Running, "RUNNING", []));
        deepDebug.RecordEvent("wire", "action_started", new { Stage = StoryWireTestRunner.Format(stage) });
        DebugRunReport report = await action(cancellationToken);
        deepDebug.RecordEvent("wire", "action_completed", new
        {
            Stage = StoryWireTestRunner.Format(stage),
            report.Succeeded,
            report.Status,
            report.Events,
            Snapshot = WireDebugEvidence.Snapshot(report.Snapshot),
        });
        progress.Report(new StoryWireProgress(
            stage,
            report.Succeeded ? StoryWireStageStatus.Passed : StoryWireStageStatus.Failed,
            report.Status,
            report.Events));
        return report.Succeeded;
    }

    public async Task<bool> WaitAsync(
        StoryWireStage stage,
        DebugStateSpec state,
        Func<CancellationToken, Task<DebugRunReport>> check,
        DebugEvidenceMode mode,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StateTimeout);
        DebugRunReport? last = null;
        WireImageStateResult? lastImage = null;
        try
        {
            while (true)
            {
                deepDebug.RecordEvent("wire", "state_poll_started", new
                {
                    Stage = StoryWireTestRunner.Format(stage),
                    State = state.Name,
                    Mode = mode.ToString(),
                });
                if (mode == DebugEvidenceMode.ImageWithOcrFallback)
                {
                    try
                    {
                        lastImage = await _hybrid.TryVerifyAsync(state, timeout.Token);
                        WireDebugEvidence.RecordComparisons(deepDebug, lastImage.Comparisons);
                        if (lastImage.IsMatch)
                        {
                            progress.Report(new StoryWireProgress(
                                stage, StoryWireStageStatus.Passed,
                                lastImage.Status, lastImage.Events, lastImage.Comparisons));
                            return true;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error)
                    {
                        lastImage = new(false, "IMAGE ERROR", [$"IMAGE FALLBACK ERROR {error.Message}"], []);
                    }
                }

                last = await check(timeout.Token);
                deepDebug.RecordEvent("ocr", "state_evaluated", new
                {
                    Stage = StoryWireTestRunner.Format(stage),
                    last.Succeeded,
                    last.Status,
                    last.Events,
                    Snapshot = WireDebugEvidence.Snapshot(last.Snapshot),
                });
                if (last.Succeeded)
                {
                    (IReadOnlyList<WireVisualComparison> Comparisons, string? Error) image =
                        await RefreshImageAsync(last, mode, timeout.Token);
                    string detail = mode == DebugEvidenceMode.Ocr
                        ? $"{last.Status} | OCR"
                        : image.Comparisons.Count == 0
                            ? $"{last.Status} | OCR FALLBACK | IMG {(image.Error is null ? "0" : "ERROR")}"
                            : $"{last.Status} | OCR FALLBACK | IMG " +
                              $"{image.Comparisons.Count(candidate => candidate.Agrees)}/{image.Comparisons.Count}";
                    List<string> events = [.. lastImage?.Events ?? [], .. last.Events];
                    events.Add(mode == DebugEvidenceMode.Ocr
                        ? "OCR PRIMARY MATCH"
                        : image.Error is null
                            ? $"OCR FALLBACK | IMAGE REFRESH " +
                              $"{image.Comparisons.Count(candidate => candidate.Agrees)}/{image.Comparisons.Count} AGREE"
                            : $"OCR FALLBACK | IMAGE ERROR {image.Error}");
                    progress.Report(new StoryWireProgress(
                        stage, StoryWireStageStatus.Passed, detail, events, image.Comparisons));
                    return true;
                }
                await Task.Delay(PollDelay, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            progress.Report(new StoryWireProgress(
                stage,
                StoryWireStageStatus.Failed,
                last?.Status ?? "STATE TIMEOUT",
                [.. lastImage?.Events ?? [], .. last?.Events ?? []]));
            return false;
        }
    }

    private async Task<(IReadOnlyList<WireVisualComparison> Comparisons, string? Error)> RefreshImageAsync(
        DebugRunReport report,
        DebugEvidenceMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == DebugEvidenceMode.Ocr) return ([], null);
        try
        {
            IReadOnlyList<WireVisualComparison> comparisons = await _hybrid.CompareAsync(report, cancellationToken);
            WireDebugEvidence.RecordComparisons(deepDebug, comparisons);
            return (comparisons, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            return ([], error.Message);
        }
    }
}
