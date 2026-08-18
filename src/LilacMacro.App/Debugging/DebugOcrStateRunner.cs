using System.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Debugging;

internal sealed class DebugOcrStateRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private readonly DebugStateDatasetContextLoader _contexts = new();

    public void EnsureAvailable()
    {
        if (workspace.IsManualCaptureActive)
        {
            throw new InvalidOperationException("Finish the manual capture before running Debug.");
        }
    }

    public async Task<DebugOcrSnapshot> RunAsync(
        DebugStateSpec state,
        string device,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        if (!ocr.IsDeviceReady(device))
        {
            throw new InvalidOperationException($"OCR {device.ToUpperInvariant()} is not set up.");
        }

        DebugStateDatasetContext context = await _contexts.LoadAsync(state, cancellationToken);
        PixelRect roi = context.RegionOfInterest;
        CapturedPng capture = await workspace.CaptureLiveFrameAsync(
            DebugWorkflowCatalog.ClientSize,
            cancellationToken);
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "LilacMacro",
            $"debug-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        string imagePath = Path.Combine(temporaryRoot, "frame.png");
        try
        {
            await File.WriteAllBytesAsync(imagePath, capture.Bytes, cancellationToken);
            OcrWorkerResult result = await ocr.RunAsync(
                imagePath,
                roi,
                OcrRunner.SmallModel,
                device,
                cancellationToken);
            OcrTextRegion[] regions = result.Regions.Select(ToRegion).ToArray();
            return new DebugOcrSnapshot(
                state.Name,
                $"{Path.GetFileName(state.DatasetDirectory)} {RegionFrames(state)}",
                roi,
                result,
                regions,
                Evaluate(state, regions),
                context.VisualAnchors);
        }
        finally
        {
            TryDelete(temporaryRoot);
        }
    }

    public async Task<DebugStateTransitionObservation> ObserveTransitionAsync(
        DebugStateSpec source,
        DebugStateSpec destination,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot destinationSnapshot = await RunAsync(
            destination,
            device,
            cancellationToken);
        if (destinationSnapshot.Evaluation.IsMatch)
        {
            return new DebugStateTransitionObservation(
                ObservedStateTransitionOutcome.DestinationReached,
                destinationSnapshot,
                null);
        }

        DebugOcrSnapshot sourceSnapshot = await RunAsync(source, device, cancellationToken);
        return new DebugStateTransitionObservation(
            ObservedStateTransitionPolicy.Classify(
                sourceSnapshot.Evaluation.IsMatch,
                destinationSnapshot.Evaluation.IsMatch),
            destinationSnapshot,
            sourceSnapshot);
    }

    public async Task<DebugOcrSnapshot> WaitForMatchAsync(
        DebugStateSpec state,
        string device,
        int maximumObservations,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumObservations, 1);
        DebugOcrSnapshot snapshot = await RunAsync(state, device, cancellationToken);
        for (int attempt = 1; !snapshot.Evaluation.IsMatch && attempt < maximumObservations; attempt++)
        {
            await Task.Delay(retryDelay, cancellationToken);
            snapshot = await RunAsync(state, device, cancellationToken);
        }
        return snapshot;
    }

    public async Task<DebugOcrSnapshot> WaitForMatchUntilDeadlineAsync(
        DebugStateSpec state,
        string device,
        CancellationToken cancellationToken)
    {
        Stopwatch retryWindow = Stopwatch.StartNew();
        DebugOcrSnapshot snapshot = await RunAsync(state, device, cancellationToken);
        while (!snapshot.Evaluation.IsMatch && MatchLoadPolicy.IsWithinRetryWindow(retryWindow.Elapsed))
        {
            TimeSpan delay = MatchLoadPolicy.RetryDelay(retryWindow.Elapsed);
            if (delay <= TimeSpan.Zero) break;
            await Task.Delay(delay, cancellationToken);
            snapshot = await RunAsync(state, device, cancellationToken);
        }
        return snapshot;
    }

    public async Task<DebugStateTransitionObservation> WaitForTransitionAsync(
        DebugStateSpec source,
        DebugStateSpec destination,
        string device,
        int maximumObservations,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumObservations, 1);
        DebugStateTransitionObservation observation = await ObserveTransitionAsync(
            source,
            destination,
            device,
            cancellationToken);
        for (int attempt = 1;
             observation.Outcome != ObservedStateTransitionOutcome.DestinationReached &&
             attempt < maximumObservations;
             attempt++)
        {
            await Task.Delay(retryDelay, cancellationToken);
            observation = await ObserveTransitionAsync(source, destination, device, cancellationToken);
        }
        return observation;
    }

    internal static OcrStateEvaluation Evaluate(
        DebugStateSpec state,
        IReadOnlyList<OcrTextRegion> regions) =>
        state.MatchMode switch
        {
            DebugMatchMode.DistinctTargets => OcrRuleEngine.Evaluate(
                state.Name,
                state.RequiredMatches,
                state.Targets,
                regions),
            DebugMatchMode.ExactTargets => OcrRuleEngine.EvaluateExact(
                state.Name,
                state.RequiredMatches,
                state.Targets,
                regions),
            DebugMatchMode.RequiredFirstTarget =>
                OcrRuleEngine.EvaluateWithRequiredFirstTarget(
                    state.Name,
                    state.RequiredMatches,
                    state.Targets,
                    regions),
            DebugMatchMode.DeclarativeEvidence => EvaluateDeclarative(state, regions),
            DebugMatchMode.RepeatedTarget when state.Targets.Count == 1 =>
                OcrRuleEngine.EvaluateRepeatedTarget(
                    state.Name,
                    state.RequiredMatches,
                    state.Targets[0],
                    regions),
            DebugMatchMode.RepeatedTarget => throw new InvalidDataException(
                $"{state.Name} repeated matching requires exactly one target."),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static OcrStateEvaluation EvaluateDeclarative(
        DebugStateSpec state,
        IReadOnlyList<OcrTextRegion> regions)
    {
        HashSet<string> required = (state.RequiredTargetNames ?? []).ToHashSet(StringComparer.Ordinal);
        HashSet<string> pool = (state.PoolTargetNames ?? []).ToHashSet(StringComparer.Ordinal);
        HashSet<string> fuzzy = (state.FuzzyPrefixTargetNames ?? []).ToHashSet(StringComparer.Ordinal);
        OcrTargetMatch[] matches = state.Targets
            .Select(target => fuzzy.Contains(target.Name)
                ? FindFuzzyPrefixTarget(target, regions)
                : OcrRuleEngine.FindTarget(target, regions))
            .Where(match => match is not null)
            .Cast<OcrTargetMatch>()
            .ToArray();
        bool requiredMatched = required.All(name => matches.Any(match => match.Target == name));
        int poolMatches = matches.Count(match => pool.Contains(match.Target));
        bool sameRowMatched = MatchesSameRow(state.SameRowTargetNames, matches);
        return new OcrStateEvaluation(
            state.Name,
            required.Count + state.MinimumPoolMatches,
            matches,
            requiredMatched && poolMatches >= state.MinimumPoolMatches && sameRowMatched);
    }

    private static bool MatchesSameRow(
        IReadOnlyList<string>? targetNames,
        IReadOnlyList<OcrTargetMatch> matches)
    {
        if (targetNames is null || targetNames.Count == 0) return true;
        OcrTargetMatch[] row = targetNames
            .Select(name => matches.FirstOrDefault(match => match.Target == name))
            .Where(match => match is not null)
            .Cast<OcrTargetMatch>()
            .ToArray();
        if (row.Length != targetNames.Count) return false;

        double tolerance = row.Max(match => match.Region.Bounds.Height) * 1.5;
        double minimumCenter = row.Min(match => CenterY(match.Region.Bounds));
        double maximumCenter = row.Max(match => CenterY(match.Region.Bounds));
        return maximumCenter - minimumCenter <= tolerance;
    }

    private static double CenterY(PixelRect bounds) => bounds.Y + bounds.Height / 2d;

    private static OcrTargetMatch? FindFuzzyPrefixTarget(
        OcrTargetRule target,
        IReadOnlyList<OcrTextRegion> regions) => regions
        .SelectMany(region => target.Aliases.Select(alias => new
        {
            Alias = alias,
            Region = region,
            Match = OcrPhraseMatcher.MatchPrefix(alias, region.Text),
        }))
        .Where(candidate => candidate.Match.IsMatch)
        .OrderByDescending(candidate => candidate.Match.Similarity)
        .ThenBy(candidate => candidate.Region.Bounds.Y)
        .Select(candidate => new OcrTargetMatch(
            target.Name,
            candidate.Alias,
            candidate.Match.ObservedNormalized,
            candidate.Region))
        .FirstOrDefault();

    private static OcrTextRegion ToRegion(OcrWorkerRegion region) => new()
    {
        Bounds = new PixelRect(region.X, region.Y, region.Width, region.Height),
        Text = region.Text,
        DetectionConfidence = region.DetectionConfidence,
        RecognitionConfidence = region.RecognitionConfidence,
    };

    private static string RegionFrames(DebugStateSpec state) =>
        string.Join("+", state.RegionFrames.Select(frame => $"F{frame}"));

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // The OS temp cleaner can remove a transient debug capture left by a locked decoder.
        }
        catch (UnauthorizedAccessException)
        {
            // The OS temp cleaner can remove a transient debug capture left by a locked decoder.
        }
    }
}
