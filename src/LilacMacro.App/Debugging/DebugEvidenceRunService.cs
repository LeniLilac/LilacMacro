using LilacMacro.App.Diagnostics;
using LilacMacro.App.Workspace;
using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal sealed record DebugEvidenceRow(
    string Text,
    string Normalized,
    string Match,
    string Bounds,
    string Confidence);

internal sealed record DebugEvidenceRunResult(
    bool Succeeded,
    string Status,
    string MatchCount,
    string Meta,
    IReadOnlyList<DebugEvidenceRow> Rows,
    IReadOnlyList<string> Events,
    DebugRunReport? OcrReport,
    WireImageStateResult? ImageResult,
    IReadOnlyList<WireVisualComparison> Comparisons);

internal sealed class DebugEvidenceRunService(
    WorkspaceController workspace,
    DeepDebugSessionService deepDebug)
{
    private readonly WireHybridEvidenceService _hybrid = new(workspace, deepDebug);

    public async Task<DebugEvidenceRunResult> RunAsync(
        DebugEvidenceMode mode,
        DebugStateSpec? imageState,
        Func<Task<DebugRunReport>> ocrOperation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ocrOperation);
        DebugEvidenceExecutionPlan plan = DebugEvidenceModePolicy.Select(mode, imageState is not null);
        WireImageStateResult? image = null;
        List<string> events = [];

        if (plan == DebugEvidenceExecutionPlan.ImageThenOcrFallback)
        {
            image = await TryImageAsync(imageState!, cancellationToken);
            events.AddRange(image.Events);
            if (image.IsMatch) return FromImage(image);
        }
        else if (plan == DebugEvidenceExecutionPlan.OcrForLiveBounds)
        {
            events.Add("IMAGE MODE | OCR REQUIRED FOR LIVE CLICK BOUNDS");
        }

        DebugRunReport report = await ocrOperation();
        IReadOnlyList<WireVisualComparison> comparisons = image?.Comparisons ?? [];
        if (plan == DebugEvidenceExecutionPlan.ImageThenOcrFallback && report.Succeeded)
        {
            try
            {
                comparisons = await _hybrid.CompareAsync(report, cancellationToken);
                events.Add($"IMAGE PROFILE REFRESH {comparisons.Count(candidate => candidate.Agrees)}/{comparisons.Count} AGREE");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                events.Add($"IMAGE PROFILE REFRESH ERROR {error.Message}");
            }
        }

        events.AddRange(report.Events);
        return FromOcr(report, plan, image, comparisons, events);
    }

    private async Task<WireImageStateResult> TryImageAsync(
        DebugStateSpec state,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _hybrid.TryVerifyAsync(state, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return new WireImageStateResult(
                false,
                "IMAGE ERROR",
                [$"IMAGE FALLBACK ERROR {error.Message}"],
                []);
        }
    }

    private static DebugEvidenceRunResult FromImage(WireImageStateResult image)
    {
        long matchMilliseconds = image.Comparisons.Sum(candidate => candidate.MatchMilliseconds);
        return new DebugEvidenceRunResult(
            true,
            image.Status,
            $"{image.MatchedCount}/{image.RequiredMatches}",
            $"IMAGE SET | {image.Comparisons.Count} ELEMENT | MATCH {matchMilliseconds} MS | OCR SKIPPED",
            image.Comparisons.Select(Present).ToArray(),
            image.Events,
            null,
            image,
            image.Comparisons);
    }

    private static DebugEvidenceRunResult FromOcr(
        DebugRunReport report,
        DebugEvidenceExecutionPlan plan,
        WireImageStateResult? image,
        IReadOnlyList<WireVisualComparison> comparisons,
        IReadOnlyList<string> events)
    {
        string prefix = plan == DebugEvidenceExecutionPlan.ImageThenOcrFallback
            ? "OCR FALLBACK | "
            : plan == DebugEvidenceExecutionPlan.OcrForLiveBounds
                ? "OCR LIVE BOUNDS | "
                : string.Empty;
        PixelRect roi = report.Snapshot.RegionOfInterest;
        string meta =
            $"{prefix}{report.Snapshot.Source} | ROI [{roi.X},{roi.Y},{roi.Width},{roi.Height}] | " +
            $"{report.Snapshot.Ocr.Device.ToUpperInvariant()} | LOAD {report.Snapshot.Ocr.ModelLoadMilliseconds} MS | " +
            $"INFERENCE {report.Snapshot.Ocr.InferenceMilliseconds} MS | {report.Snapshot.Ocr.Regions.Length} BOX";
        DebugEvidenceRow[] rows =
        [
            .. comparisons.Select(Present),
            .. report.Snapshot.Ocr.Regions.Select(region => Present(region, report)),
        ];
        return new DebugEvidenceRunResult(
            report.Succeeded,
            plan == DebugEvidenceExecutionPlan.ImageThenOcrFallback
                ? $"{report.Status} | OCR FALLBACK"
                : report.Status,
            $"{report.Snapshot.Evaluation.Matches.Count}/{report.Snapshot.Evaluation.RequiredMatches}",
            meta,
            rows,
            events,
            report,
            image,
            comparisons);
    }

    private static DebugEvidenceRow Present(WireVisualComparison comparison) => new(
        comparison.Label,
        comparison.Strategy,
        comparison.ImageStatus,
        comparison.ImageBounds,
        $"IMG {comparison.Score:P1} | {comparison.MatchMilliseconds} MS");

    private static DebugEvidenceRow Present(OcrWorkerRegion region, DebugRunReport report)
    {
        PixelRect bounds = new(region.X, region.Y, region.Width, region.Height);
        string matches = string.Join(", ", report.Snapshot.Evaluation.Matches
            .Where(match => match.Region.Bounds == bounds && match.Region.Text == region.Text)
            .Select(match => $"{match.Target} ({match.Alias})"));
        return new DebugEvidenceRow(
            region.Text,
            OcrRuleEngine.Normalize(region.Text),
            matches,
            $"[{region.X},{region.Y},{region.Width},{region.Height}]",
            $"OCR {region.RecognitionConfidence:P1}");
    }
}
