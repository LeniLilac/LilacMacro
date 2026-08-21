namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugEvidenceRetention
{
    private readonly List<DeepDebugEvidenceFrame> _frames = [];
    private readonly List<DeepDebugEvidenceWindow> _windows = [];
    private readonly List<string> _pendingTransitions = [];
    private int _nextWindowId;
    private bool _optimized;

    public int RetainedFrameCount => _frames.Count(frame => !frame.Deleted);
    public int DiscardedFrameCount { get; private set; }
    public int WindowCount => _windows.Count;
    public int DiscardedWindowCount { get; private set; }
    public int TransitionFrameCount => _frames.Count(frame => !frame.Deleted && frame.Transitions.Count > 0);
    public long RetainedBytes => _frames.Where(frame => !frame.Deleted).Sum(frame => frame.Length);
    public bool IsOptimized => _optimized;

    public void ObserveEvent(string category, string action, object? data, DateTimeOffset timestampUtc)
    {
        if (DeepDebugEvidencePolicy.TryClassifyError(
                category, action, data, timestampUtc, out DeepDebugErrorMarker? error))
            BeginErrorWindow(error!);
        if (DeepDebugEvidencePolicy.TransitionSignature(category, action) is { } transition)
            MarkTransition(transition);
    }

    public void RecordFrame(
        string path,
        DateTimeOffset timestampUtc,
        byte[] png,
        bool fullClient)
    {
        DeepDebugEvidenceFrame frame = new(
            path,
            timestampUtc,
            png.LongLength,
            DeepDebugPerceptualHash.Create(png),
            fullClient);
        _frames.Add(frame);

        DeepDebugEvidenceWindow? active = _windows.LastOrDefault();
        if (active is not null && timestampUtc >= active.StartUtc && timestampUtc <= active.EndUtc)
        {
            active.Add(frame);
        }
        if (_pendingTransitions.Count > 0)
        {
            foreach (string transition in _pendingTransitions) frame.Transitions.Add(transition);
            _pendingTransitions.Clear();
        }
    }

    public void Complete(long maximumFrameBytes)
    {
        if (RetainedBytes <= maximumFrameBytes) return;
        _optimized = true;
        TrimToBudget(maximumFrameBytes);
    }

    public bool DropLowestPriorityEvidence()
    {
        foreach (DeepDebugEvidenceFrame ordinary in _frames
            .Where(frame => !frame.Deleted && frame.WindowId is null && frame.Transitions.Count == 0)
            .OrderBy(frame => frame.TimestampUtc))
        {
            if (Delete(ordinary)) return true;
        }

        foreach (DeepDebugEvidenceFrame transition in _frames
            .Where(frame => !frame.Deleted && frame.WindowId is null)
            .OrderBy(frame => frame.TimestampUtc))
        {
            if (Delete(transition)) return true;
        }

        DeepDebugEvidenceWindow? window = _windows
            .Where(candidate => candidate.Frames.Any(frame => !frame.Deleted))
            .OrderBy(candidate => candidate.MaximumSeverity)
            .ThenBy(candidate => candidate.IsFirstOccurrence)
            .ThenBy(candidate => RedundancyDistance(candidate))
            .ThenBy(candidate => candidate.StartUtc)
            .FirstOrDefault();
        if (window is null) return false;
        DeepDebugEvidenceFrame? leastUsefulFrame = window.Frames
            .Where(frame => !frame.Deleted)
            .OrderByDescending(frame => window.Errors.Min(error =>
                Math.Abs((frame.TimestampUtc - error.TimestampUtc).Ticks)))
            .ThenBy(frame => frame.TimestampUtc)
            .FirstOrDefault();
        if (leastUsefulFrame is null || !Delete(leastUsefulFrame)) return false;
        if (!window.Frames.Any(frame => !frame.Deleted)) DiscardedWindowCount++;
        return true;
    }

    public long DropLowestPriorityEvidence(long minimumBytes)
    {
        long before = RetainedBytes;
        while (before - RetainedBytes < minimumBytes && DropLowestPriorityEvidence()) { }
        long discarded = before - RetainedBytes;
        if (discarded > 0) _optimized = true;
        return discarded;
    }

    public void OptimizeWhenAbove(long maximumFrameBytes)
    {
        if (RetainedBytes > maximumFrameBytes)
        {
            _optimized = true;
            TrimToBudget(maximumFrameBytes);
        }
    }

    private void TrimToBudget(long maximumBytes)
    {
        while (RetainedBytes > maximumBytes && DropLowestPriorityEvidence()) { }
    }

    private void BeginErrorWindow(DeepDebugErrorMarker marker)
    {
        DateTimeOffset start = marker.TimestampUtc - DeepDebugEvidencePolicy.ErrorWindowBefore;
        DateTimeOffset end = marker.TimestampUtc + DeepDebugEvidencePolicy.ErrorWindowAfter;
        DeepDebugEvidenceWindow? window = _windows.LastOrDefault();
        if (window is null || start > window.EndUtc)
        {
            bool first = _windows.All(candidate => candidate.Errors.All(error =>
                !string.Equals(error.Signature, marker.Signature, StringComparison.Ordinal)));
            window = new(++_nextWindowId, start, end, first);
            _windows.Add(window);
        }
        else
        {
            window.EndUtc = end > window.EndUtc ? end : window.EndUtc;
        }
        window.Errors.Add(marker);
        foreach (DeepDebugEvidenceFrame frame in _frames.Where(frame =>
                     !frame.Deleted && frame.TimestampUtc >= window.StartUtc &&
                     frame.TimestampUtc <= marker.TimestampUtc))
            window.Add(frame);
    }

    private void MarkTransition(string signature)
    {
        DeepDebugEvidenceFrame? frame = _frames
            .Where(candidate => !candidate.Deleted && candidate.FullClient)
            .OrderByDescending(candidate => candidate.TimestampUtc)
            .FirstOrDefault() ?? _frames
            .Where(candidate => !candidate.Deleted)
            .OrderByDescending(candidate => candidate.TimestampUtc)
            .FirstOrDefault();
        if (frame is null) _pendingTransitions.Add(signature);
        else frame.Transitions.Add(signature);
    }

    private int RedundancyDistance(DeepDebugEvidenceWindow candidate)
    {
        return _windows
            .Where(window => window.Id != candidate.Id &&
                             window.Frames.Any(frame => !frame.Deleted))
            .Select(window => DeepDebugPerceptualHash.Distance(
                candidate.RepresentativeHash,
                window.RepresentativeHash))
            .DefaultIfEmpty(64)
            .Min();
    }

    private bool Delete(DeepDebugEvidenceFrame frame)
    {
        if (frame.Deleted) return false;
        try
        {
            if (File.Exists(frame.Path)) File.Delete(frame.Path);
            frame.Deleted = true;
            DiscardedFrameCount++;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Finalization can retry after ZIP creation fails; never hide the primary operation.
            return false;
        }
    }
}

internal sealed class DeepDebugEvidenceWindow(
    int id,
    DateTimeOffset startUtc,
    DateTimeOffset endUtc,
    bool isFirstOccurrence)
{
    public int Id { get; } = id;
    public DateTimeOffset StartUtc { get; } = startUtc;
    public DateTimeOffset EndUtc { get; set; } = endUtc;
    public bool IsFirstOccurrence { get; } = isFirstOccurrence;
    public List<DeepDebugErrorMarker> Errors { get; } = [];
    public List<DeepDebugEvidenceFrame> Frames { get; } = [];
    public DeepDebugErrorSeverity MaximumSeverity => Errors.Max(error => error.Severity);
    public DateTimeOffset FirstErrorAtUtc => Errors.Min(error => error.TimestampUtc);
    public ulong RepresentativeHash => Frames
        .Where(frame => !frame.Deleted)
        .OrderBy(frame => Math.Abs((frame.TimestampUtc - FirstErrorAtUtc).Ticks))
        .Select(frame => frame.PerceptualHash)
        .FirstOrDefault();

    public void Add(DeepDebugEvidenceFrame frame)
    {
        if (frame.WindowId == Id) return;
        frame.WindowId = Id;
        Frames.Add(frame);
    }
}

internal sealed class DeepDebugEvidenceFrame(
    string path,
    DateTimeOffset timestampUtc,
    long length,
    ulong perceptualHash,
    bool fullClient)
{
    public string Path { get; } = path;
    public DateTimeOffset TimestampUtc { get; } = timestampUtc;
    public long Length { get; } = length;
    public ulong PerceptualHash { get; } = perceptualHash;
    public bool FullClient { get; } = fullClient;
    public int? WindowId { get; set; }
    public HashSet<string> Transitions { get; } = new(StringComparer.Ordinal);
    public bool Deleted { get; set; }
}
