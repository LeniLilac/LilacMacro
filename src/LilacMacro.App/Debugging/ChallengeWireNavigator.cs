using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal sealed class ChallengeWireNavigator(
    WorkspaceController workspace,
    OcrRunner ocr,
    DeepDebugSessionService deepDebug)
{
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(400);
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly WireHybridEvidenceService _hybrid = new(workspace, deepDebug);
    private readonly ChallengeRotationStore _store = new();

    public async Task<ChallengeNavigationResult> NavigateAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        RegularChallengeType[] enabled = options.ChallengeTypes.Distinct().ToArray();
        if (enabled.Length == 0)
            return Failed("NO CHALLENGE TYPES ENABLED");

        ChallengeRotationPolicy rotation = new(await _store.LoadAsync(cancellationToken));
        foreach (RegularChallengeType type in enabled)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!rotation.CanAttempt(type, now)) continue;
            if (!await SelectTypeAsync(type, options.Device, progress, cancellationToken))
                return Failed($"{type.ToString().ToUpperInvariant()} SELECT BLOCKED");

            ChallengeSelection? selection = await WaitForSelectionAsync(options, progress, cancellationToken);
            if (selection is null) return Failed("CHALLENGE STATE TIMEOUT");
            if (selection.Kind == ChallengeSelectionKind.Available)
            {
                rotation.ObserveAvailable(type, DateTimeOffset.UtcNow);
                await _store.SaveAsync(rotation.Snapshot(DateTimeOffset.UtcNow), cancellationToken);
                await ClickAsync(selection.Snapshot, "Select Stage", options.Device, cancellationToken);
                string map = FindMap(selection.Snapshot)
                    ?? throw new InvalidDataException("Challenge state has no supported map evidence.");
                Report(progress, StoryWireStage.ChallengeState, StoryWireStageStatus.Passed,
                    $"{type.ToString().ToUpperInvariant()} AVAILABLE | {map}", selection.Events, selection.Comparisons);
                return new ChallengeNavigationResult(true, "CHALLENGE AVAILABLE", map, type, null, false);
            }

            bool dailyLimit = rotation.ObserveCooldown(type, DateTimeOffset.UtcNow);
            await _store.SaveAsync(rotation.Snapshot(DateTimeOffset.UtcNow), cancellationToken);
            await ClickAsync(selection.Snapshot, "Back", options.Device, cancellationToken);
            Report(progress, StoryWireStage.ChallengeState, StoryWireStageStatus.Passed,
                dailyLimit
                    ? $"{type.ToString().ToUpperInvariant()} 10/10 UNTIL UTC MIDNIGHT"
                    : $"{type.ToString().ToUpperInvariant()} COOLDOWN",
                selection.Events,
                selection.Comparisons);
            if (!await WaitForTypePickerAsync(options.Device, cancellationToken))
                return Failed("CHALLENGE TYPE RETURN BLOCKED");
        }

        DateTimeOffset until = rotation.NextEligibleUtc(enabled, DateTimeOffset.UtcNow);
        bool allDaily = enabled.All(type => rotation.IsDailyLimited(type, DateTimeOffset.UtcNow));
        string status = allDaily
            ? $"CHALLENGE 10/10 | NEXT UTC DAY {until:yyyy-MM-dd HH:mm}Z"
            : $"CHALLENGE COOLDOWN | NEXT RESET {until:HH:mm}Z";
        Report(progress, StoryWireStage.ChallengeState, StoryWireStageStatus.Passed, status, [status], []);
        return new ChallengeNavigationResult(true, status, null, null, until, allDaily);
    }

    private async Task<bool> SelectTypeAsync(
        RegularChallengeType type,
        string device,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        Report(progress, StoryWireStage.ChallengeType, StoryWireStageStatus.Running,
            $"SELECT {type.ToString().ToUpperInvariant()}", [], []);
        DebugRunReport report = await _debug.SelectChallengeTypeAsync(type, device, cancellationToken);
        deepDebug.RecordEvent("challenge", "type_selected", new
        {
            Type = type.ToString(),
            report.Succeeded,
            report.Status,
            Snapshot = WireDebugEvidence.Snapshot(report.Snapshot),
        });
        Report(progress, StoryWireStage.ChallengeType,
            report.Succeeded ? StoryWireStageStatus.Passed : StoryWireStageStatus.Failed,
            report.Status, report.Events, []);
        return report.Succeeded;
    }

    private async Task<ChallengeSelection?> WaitForSelectionAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StateTimeout);
        try
        {
            while (true)
            {
                Report(progress, StoryWireStage.ChallengeState, StoryWireStageStatus.Running,
                    "CHECK AVAILABLE / COOLDOWN", [], []);
                ChallengeSelection? selection = await TryStateAsync(
                    ChallengeSelectionKind.Available,
                    DebugWorkflowCatalog.ChallengeAvailable,
                    options,
                    timeout.Token);
                selection ??= await TryStateAsync(
                    ChallengeSelectionKind.Cooldown,
                    DebugWorkflowCatalog.ChallengeCooldown,
                    options,
                    timeout.Token);
                if (selection is not null) return selection;
                await Task.Delay(PollDelay, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<ChallengeSelection?> TryStateAsync(
        ChallengeSelectionKind kind,
        DebugStateSpec state,
        StoryWireTestOptions options,
        CancellationToken cancellationToken)
    {
        List<string> events = [];
        IReadOnlyList<WireVisualComparison> comparisons = [];
        if (options.Mode == DebugEvidenceMode.ImageWithOcrFallback)
        {
            try
            {
                WireImageStateResult image = await _hybrid.TryVerifyAsync(state, cancellationToken);
                comparisons = image.Comparisons;
                events.AddRange(image.Events);
                WireDebugEvidence.RecordComparisons(deepDebug, comparisons);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                events.Add($"IMAGE ERROR {error.Message}");
            }
        }

        DebugOcrSnapshot snapshot = await _states.RunAsync(state, options.Device, cancellationToken);
        deepDebug.RecordEvent("challenge", "selection_state_evaluated", new
        {
            Kind = kind.ToString(),
            snapshot.Evaluation.IsMatch,
            Snapshot = WireDebugEvidence.Snapshot(snapshot),
        });
        if (!snapshot.Evaluation.IsMatch) return null;
        events.Add(options.Mode == DebugEvidenceMode.Ocr ? "OCR PRIMARY MATCH" : "OCR FALLBACK MATCH");
        return new ChallengeSelection(kind, snapshot, events, comparisons);
    }

    private async Task<bool> WaitForTypePickerAsync(string device, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StateTimeout);
        try
        {
            while (true)
            {
                DebugRunReport report = await _debug.CheckChallengeTypesAsync(device, timeout.Token);
                if (report.Succeeded) return true;
                await Task.Delay(PollDelay, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task ClickAsync(
        DebugOcrSnapshot snapshot,
        string target,
        string device,
        CancellationToken cancellationToken)
    {
        OcrTargetMatch match = snapshot.Evaluation.Matches.FirstOrDefault(candidate => candidate.Target == target)
            ?? throw new InvalidDataException($"Challenge state is missing {target}.");
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, match.Region.Bounds.Center, cancellationToken);
        deepDebug.RecordEvent("challenge", "anchor_clicked", new
        {
            Target = target,
            match.Region.Bounds,
            Device = device,
        });
    }

    private static string? FindMap(DebugOcrSnapshot snapshot)
    {
        HashSet<string> names = DebugWorkflowCatalog.MapTargets.Select(target => target.Name).ToHashSet();
        return snapshot.Evaluation.Matches.FirstOrDefault(match => names.Contains(match.Target))?.Target;
    }

    private static ChallengeNavigationResult Failed(string status) =>
        new(false, status, null, null, null, false);

    private static void Report(
        IProgress<StoryWireProgress> progress,
        StoryWireStage stage,
        StoryWireStageStatus status,
        string detail,
        IReadOnlyList<string> events,
        IReadOnlyList<WireVisualComparison> comparisons) =>
        progress.Report(new StoryWireProgress(stage, status, detail, events, comparisons));

    private enum ChallengeSelectionKind { Available, Cooldown }

    private sealed record ChallengeSelection(
        ChallengeSelectionKind Kind,
        DebugOcrSnapshot Snapshot,
        IReadOnlyList<string> Events,
        IReadOnlyList<WireVisualComparison> Comparisons);
}
