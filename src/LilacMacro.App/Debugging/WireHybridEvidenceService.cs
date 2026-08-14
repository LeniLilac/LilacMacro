using System.Diagnostics;
using System.Text.Json;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Vision;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Debugging;

internal sealed class WireHybridEvidenceService(
    WorkspaceController workspace,
    DeepDebugSessionService deepDebug)
{
    private const int BurstSamples = 5;
    private static readonly TimeSpan BurstDelay = TimeSpan.FromMilliseconds(100);
    private readonly VisualFingerprintBuilder _builder = new();
    private readonly VisualAnchorRegionMatcher _matcher = new();
    private readonly VisualProfileStore _profiles = new();
    private readonly DebugStateDatasetContextLoader _contexts = new();
    private readonly WireVisualLocatorStore _locators = new();
    private readonly string _profileRoot = ResolveProfileRoot();

    private static string ResolveProfileRoot() =>
        Environment.GetEnvironmentVariable("LILACMACRO_RUNNER_VISUAL_PROFILES") is { Length: > 0 } value
            ? Path.GetFullPath(value)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LilacMacro",
                "visual-profiles",
                "wire");

    public async Task<IReadOnlyList<WireVisualComparison>> CompareAsync(
        DebugRunReport report,
        CancellationToken cancellationToken)
    {
        List<LiveAnchor> anchors = ResolveAnchors(report.Snapshot);
        if (anchors.Count == 0) return [];

        IReadOnlyList<PixelRect> sourceBounds = anchors.Select(anchor => anchor.Region.Bounds).ToArray();
        List<IReadOnlyList<CapturedGrayRegion>> bursts = [];
        for (int index = 0; index < BurstSamples; index++)
        {
            bursts.Add(await workspace.CaptureDetectorRegionsAsync(
                DebugWorkflowCatalog.ClientSize,
                sourceBounds,
                cancellationToken));
            if (index + 1 < BurstSamples) await Task.Delay(BurstDelay, cancellationToken);
        }

        List<BuiltAnchor> built = [];
        for (int index = 0; index < anchors.Count; index++)
        {
            LiveAnchor anchor = anchors[index];
            VisualAnchorSample[] samples = bursts
                .Select(burst => burst[index])
                .Select(capture => new VisualAnchorSample(
                    EmbedInClient(capture),
                    capture.Region))
                .ToArray();
            VisualAnchorDefinition definition = new(
                ProfileId(
                    report.Snapshot.State,
                    $"{anchor.Intent.Text}-{anchor.Intent.SpatialSelector}"),
                [anchor.Intent.Text]);
            Stopwatch buildTimer = Stopwatch.StartNew();
            VisualAnchorProfile profile = _builder.Build(definition, samples, DateTimeOffset.UtcNow);
            buildTimer.Stop();
            string revisionDirectory = await _profiles.SaveRevisionAsync(
                _profileRoot,
                profile,
                cancellationToken);
            string locatorPath = await _locators.SaveAsync(
                _profileRoot,
                new WireVisualLocator(
                    1,
                    definition.Id,
                    report.Snapshot.State,
                    anchor.Intent.Text,
                    anchor.Region.Bounds),
                cancellationToken);
            deepDebug.RecordVisualProfileRevision(
                definition.Id,
                revisionDirectory,
                locatorPath);
            PixelRect search = VisualAnchorRegionMatcher.GetCaptureBounds(
                DebugWorkflowCatalog.ClientSize,
                anchor.Region.Bounds);
            built.Add(new BuiltAnchor(anchor, profile, search, buildTimer.ElapsedMilliseconds));
        }

        IReadOnlyList<CapturedGrayRegion> evaluations = await workspace.CaptureDetectorRegionsAsync(
            DebugWorkflowCatalog.ClientSize,
            built.Select(anchor => anchor.SearchBounds).ToArray(),
            cancellationToken);
        List<WireVisualComparison> results = [];
        for (int index = 0; index < built.Count; index++)
        {
            BuiltAnchor anchor = built[index];
            CapturedGrayRegion capture = evaluations[index];
            Stopwatch matchTimer = Stopwatch.StartNew();
            VisualAnchorMatchResult match = _matcher.Match(
                capture.Image,
                capture.Region,
                anchor.Profile,
                anchor.Live.Region.Bounds);
            matchTimer.Stop();
            bool agrees = match.IsMatch && match.Bounds is { } imageBounds &&
                IntersectionOverUnion(anchor.Live.Region.Bounds, imageBounds) >= 0.5;
            results.Add(new WireVisualComparison(
                report.Snapshot.State,
                DisplayLabel(anchor.Live.Intent),
                Format(anchor.Live.Region.Bounds),
                match.Bounds is { } bounds ? Format(bounds) : "NONE",
                match.Status.ToString().ToUpperInvariant(),
                match.Score,
                report.Snapshot.Ocr.InferenceMilliseconds,
                anchor.BuildMilliseconds,
                matchTimer.ElapsedMilliseconds,
                anchor.Profile.Strategy.ToString().ToUpperInvariant(),
                agrees,
                anchor.Profile.MedianTemplate,
                anchor.Profile.GrayReliability,
                Crop(capture, match.Bounds ?? anchor.Live.Region.Bounds)));
        }
        return results;
    }

    public async Task<WireImageStateResult> TryVerifyAsync(
        DebugStateSpec state,
        CancellationToken cancellationToken)
    {
        DebugStateDatasetContext context = await _contexts.LoadAsync(state, cancellationToken);
        if (context.VisualAnchors.Count == 0)
        {
            return Unavailable(state, "NO IMAGE ELEMENTS");
        }

        List<CachedAnchor> cached = [];
        List<string> events = [];
        foreach (DebugVisualAnchorIntent intent in context.VisualAnchors)
        {
            string id = ProfileId(state.Name, $"{intent.Text}-{intent.SpatialSelector}");
            try
            {
                LoadedVisualProfileRevision loaded = await _profiles.LoadCurrentRevisionAsync(
                    _profileRoot,
                    id,
                    cancellationToken);
                VisualAnchorProfile profile = loaded.Profile;
                deepDebug.RecordVisualProfileRevision(
                    id,
                    loaded.RevisionDirectory,
                    LocatorPath(id));
                WireVisualLocator locator = await _locators.LoadAsync(
                    _profileRoot,
                    id,
                    cancellationToken);
                if (locator.Version != 1 || locator.ProfileId != id || locator.State != state.Name ||
                    !locator.Bounds.IsInside(DebugWorkflowCatalog.ClientSize))
                {
                    throw new InvalidDataException("Visual locator does not match its profile.");
                }
                PixelRect search = VisualAnchorRegionMatcher.GetCaptureBounds(
                    DebugWorkflowCatalog.ClientSize,
                    locator.Bounds);
                cached.Add(new CachedAnchor(intent, profile, locator.Bounds, search));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                events.Add($"IMAGE FALLBACK {DisplayLabel(intent)} {ShortError(error)}");
            }
        }
        if (cached.Count == 0)
        {
            return new WireImageStateResult(false, "IMAGE PROFILE MISSING", events, []);
        }

        IReadOnlyList<CapturedGrayRegion> captures = await workspace.CaptureDetectorRegionsAsync(
            DebugWorkflowCatalog.ClientSize,
            cached.Select(anchor => anchor.SearchBounds).ToArray(),
            cancellationToken);
        List<OcrTextRegion> matchedRegions = [];
        List<WireVisualComparison> comparisons = [];
        for (int index = 0; index < cached.Count; index++)
        {
            CachedAnchor anchor = cached[index];
            CapturedGrayRegion capture = captures[index];
            Stopwatch timer = Stopwatch.StartNew();
            VisualAnchorMatchResult match = _matcher.Match(
                capture.Image,
                capture.Region,
                anchor.Profile,
                anchor.ExpectedBounds);
            timer.Stop();
            bool reliable = match.IsMatch && match.Bounds is { } bounds &&
                IntersectionOverUnion(anchor.ExpectedBounds, bounds) >= 0.5;
            if (reliable && match.Bounds is { } matchedBounds)
            {
                matchedRegions.Add(new OcrTextRegion
                {
                    Text = anchor.Intent.Text,
                    Bounds = matchedBounds,
                    DetectionConfidence = match.Score,
                    RecognitionConfidence = match.Score,
                });
            }
            comparisons.Add(new WireVisualComparison(
                state.Name,
                DisplayLabel(anchor.Intent),
                $"CACHED {Format(anchor.ExpectedBounds)}",
                match.Bounds is { } imageBounds ? Format(imageBounds) : "NONE",
                match.Status.ToString().ToUpperInvariant(),
                match.Score,
                0,
                0,
                timer.ElapsedMilliseconds,
                anchor.Profile.Strategy.ToString().ToUpperInvariant(),
                reliable,
                anchor.Profile.MedianTemplate,
                anchor.Profile.GrayReliability,
                Crop(capture, match.Bounds ?? anchor.ExpectedBounds)));
        }

        OcrStateEvaluation evaluation = DebugOcrStateRunner.Evaluate(state, matchedRegions);
        bool isMatch = evaluation.IsMatch;
        events.Add(isMatch
            ? $"IMAGE PRIMARY {evaluation.Matches.Count}/{evaluation.RequiredMatches} MATCH"
            : $"IMAGE FALLBACK {evaluation.Matches.Count}/{evaluation.RequiredMatches} MATCH");
        return new WireImageStateResult(
            isMatch,
            isMatch ? $"{state.Name} TRUE | IMAGE" : "IMAGE INCOMPLETE",
            events,
            comparisons,
            evaluation.Matches.Count,
            evaluation.RequiredMatches);
    }

    private static List<LiveAnchor> ResolveAnchors(DebugOcrSnapshot snapshot)
    {
        List<LiveAnchor> resolved = [];
        HashSet<PixelRect> ownedBounds = [];
        foreach (DebugVisualAnchorIntent intent in snapshot.VisualAnchors)
        {
            OcrTextRegion? region = Resolve(intent, snapshot.Regions);
            if (region is null || region.Bounds.Width < 8 || region.Bounds.Height < 8 ||
                !ownedBounds.Add(region.Bounds)) continue;
            resolved.Add(new LiveAnchor(intent, region));
        }
        return resolved;
    }

    private static OcrTextRegion? Resolve(
        DebugVisualAnchorIntent intent,
        IReadOnlyList<OcrTextRegion> regions) => OcrSpatialSelectorPolicy.Select(
        new OcrTextRegion
        {
            Bounds = new PixelRect(0, 0, 1, 1),
            Text = intent.Text,
            RecognitionConfidence = 1,
            IsVisualAnchor = true,
            MatchMode = intent.MatchMode,
            SpatialSelector = intent.SpatialSelector,
            SpatialAnchorText = intent.SpatialAnchorText,
        },
        regions);

    private static double IntersectionOverUnion(PixelRect first, PixelRect second)
    {
        int left = Math.Max(first.X, second.X);
        int top = Math.Max(first.Y, second.Y);
        int right = Math.Min(first.Right, second.Right);
        int bottom = Math.Min(first.Bottom, second.Bottom);
        long intersection = Math.Max(0, right - left) * (long)Math.Max(0, bottom - top);
        long union = first.Width * (long)first.Height + second.Width * (long)second.Height - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static GrayImage EmbedInClient(CapturedGrayRegion capture)
    {
        PixelSize client = DebugWorkflowCatalog.ClientSize;
        byte[] pixels = new byte[checked(client.Width * client.Height)];
        ReadOnlySpan<byte> source = capture.Image.Pixels.Span;
        for (int row = 0; row < capture.Region.Height; row++)
        {
            source.Slice(row * capture.Region.Width, capture.Region.Width).CopyTo(
                pixels.AsSpan((capture.Region.Y + row) * client.Width + capture.Region.X, capture.Region.Width));
        }
        return new GrayImage(client.Width, client.Height, pixels);
    }

    private static GrayImage Crop(CapturedGrayRegion capture, PixelRect clientBounds)
    {
        PixelRect local = new(
            clientBounds.X - capture.Region.X,
            clientBounds.Y - capture.Region.Y,
            clientBounds.Width,
            clientBounds.Height);
        if (!local.IsInside(new PixelSize(capture.Image.Width, capture.Image.Height)))
        {
            throw new InvalidDataException("Visual match crop is outside the captured search region.");
        }
        byte[] pixels = new byte[checked(local.Width * local.Height)];
        for (int row = 0; row < local.Height; row++)
        {
            capture.Image.Pixels.Span.Slice(
                (local.Y + row) * capture.Image.Width + local.X,
                local.Width).CopyTo(pixels.AsSpan(row * local.Width, local.Width));
        }
        return new GrayImage(local.Width, local.Height, pixels);
    }

    private static string ProfileId(string state, string label)
    {
        string id = $"wire-{state}-{label}".ToLowerInvariant();
        id = new string(id.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray());
        while (id.Contains("--", StringComparison.Ordinal)) id = id.Replace("--", "-", StringComparison.Ordinal);
        return id.Trim('-')[..Math.Min(id.Trim('-').Length, 128)];
    }

    private static string DisplayLabel(DebugVisualAnchorIntent intent) =>
        intent.SpatialSelector == OcrSpatialSelector.Any
            ? intent.Text
            : $"{intent.Text} ({intent.SpatialSelector.ToString().ToUpperInvariant()})";

    private static string Format(PixelRect bounds) =>
        $"[{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}]";

    private string LocatorPath(string profileId) => _locators.PathFor(_profileRoot, profileId);

    private static WireImageStateResult Unavailable(DebugStateSpec state, string reason) => new(
        false,
        reason,
        [$"IMAGE FALLBACK {state.Name} {reason}"],
        []);

    private static string ShortError(Exception error) => error switch
    {
        FileNotFoundException or DirectoryNotFoundException => "PROFILE MISSING",
        InvalidDataException or JsonException => "PROFILE INVALID",
        UnauthorizedAccessException => "PROFILE UNREADABLE",
        _ => "PROFILE UNAVAILABLE",
    };

    private sealed record LiveAnchor(DebugVisualAnchorIntent Intent, OcrTextRegion Region);

    private sealed record BuiltAnchor(
        LiveAnchor Live,
        VisualAnchorProfile Profile,
        PixelRect SearchBounds,
        long BuildMilliseconds);

    private sealed record CachedAnchor(
        DebugVisualAnchorIntent Intent,
        VisualAnchorProfile Profile,
        PixelRect ExpectedBounds,
        PixelRect SearchBounds);

}
