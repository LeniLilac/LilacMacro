namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugEvidenceRetention
{
    private readonly List<DeepDebugEvidenceFrame> _frames = [];
    private readonly List<DeepDebugEvidenceWindow> _windows = [];
    private readonly List<string> _pendingTransitions = [];
    private int _nextWindowId;

    public int RetainedFrameCount => _frames.Count(frame => !frame.Deleted);
    public int DiscardedFrameCount { get; private set; }
    public int WindowCount => _windows.Count;
    public int DiscardedWindowCount { get; private set; }
    public int TransitionFrameCount => _frames.Count(frame => !frame.Deleted && frame.Transitions.Count > 0);
    public long RetainedBytes => _frames.Where(frame => !frame.Deleted).Sum(frame => frame.Length);

    public void ObserveEvent(string category, string action, object? data, DateTimeOffset timestampUtc)
    {
        if (DeepDebugEvidencePolicy.TryClassifyError(
                category, action, data, timestampUtc, out DeepDebugErrorMarker? error))
            BeginErrorWindow(error!);
        if (DeepDebugEvidencePolicy.TransitionSignature(category, action) is { } transition)
            MarkTransition(transition);
        PruneExpiredPending(timestampUtc);
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
        PruneExpiredPending(timestampUtc);
    }

    public void Complete(DateTimeOffset completedAtUtc, long maximumFrameBytes)
    {
        PruneExpiredPending(completedAtUtc + DeepDebugEvidencePolicy.ErrorWindowBefore);
        foreach (DeepDebugEvidenceFrame frame in _frames.Where(frame =>
                     !frame.Deleted && frame.WindowId is null && frame.Transitions.Count == 0))
            Delete(frame);

        HashSet<int> selectedWindows = SelectWindows(maximumFrameBytes);
        foreach (DeepDebugEvidenceWindow window in _windows)
        {
            if (selectedWindows.Contains(window.Id)) continue;
            DiscardedWindowCount++;
            foreach (DeepDebugEvidenceFrame frame in window.Frames)
                if (frame.Transitions.Count == 0) Delete(frame);
        }

        long retained = RetainedBytes;
        foreach (DeepDebugEvidenceFrame frame in _frames
                     .Where(frame => !frame.Deleted && frame.WindowId is null)
                     .OrderByDescending(frame => frame.TimestampUtc))
        {
            if (retained <= maximumFrameBytes) break;
            retained -= frame.Length;
            Delete(frame);
        }
    }

    public bool DropLowestPriorityEvidence()
    {
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
            .ThenBy(candidate => candidate.StartUtc)
            .FirstOrDefault();
        if (window is null) return false;
        bool deleted = false;
        foreach (DeepDebugEvidenceFrame frame in window.Frames)
            deleted |= Delete(frame);
        if (!deleted) return false;
        DiscardedWindowCount++;
        return true;
    }

    public void TrimToBudget(long maximumBytes)
    {
        while (RetainedBytes > maximumBytes && DropLowestPriorityEvidence())
        {
        }
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

    private HashSet<int> SelectWindows(long maximumBytes)
    {
        HashSet<int> selected = [];
        long retained = _frames.Where(frame => !frame.Deleted && frame.WindowId is null)
            .Sum(frame => frame.Length);
        List<DeepDebugEvidenceWindow> remaining = _windows
            .OrderByDescending(window => window.MaximumSeverity)
            .ThenByDescending(window => window.IsFirstOccurrence)
            .ThenBy(window => window.StartUtc)
            .ToList();
        List<ulong> selectedHashes = [];

        while (remaining.Count > 0)
        {
            DeepDebugEvidenceWindow candidate = remaining
                .OrderByDescending(window => WindowPriority(window, selectedHashes))
                .ThenByDescending(window => window.StartUtc)
                .First();
            remaining.Remove(candidate);
            long additional = candidate.Frames
                .Where(frame => !frame.Deleted && frame.WindowId == candidate.Id)
                .Sum(frame => frame.Length);
            if (retained + additional > maximumBytes) continue;
            selected.Add(candidate.Id);
            retained += additional;
            selectedHashes.Add(candidate.RepresentativeHash);
        }

        if (selected.Count == 0 && _windows.Count > 0)
        {
            DeepDebugEvidenceWindow first = _windows
                .OrderByDescending(window => window.MaximumSeverity)
                .ThenByDescending(window => window.IsFirstOccurrence)
                .First();
            DeepDebugEvidenceFrame? representative = first.Frames
                .Where(frame => !frame.Deleted)
                .OrderBy(frame => Math.Abs((frame.TimestampUtc - first.FirstErrorAtUtc).Ticks))
                .FirstOrDefault(frame => retained + frame.Length <= maximumBytes);
            if (representative is not null)
            {
                representative.WindowId = null;
                representative.Transitions.Add("error-window-representative");
            }
        }
        return selected;
    }

    private static long WindowPriority(
        DeepDebugEvidenceWindow window,
        IReadOnlyList<ulong> selectedHashes)
    {
        int distance = selectedHashes.Count == 0
            ? 64
            : selectedHashes.Min(hash => DeepDebugPerceptualHash.Distance(hash, window.RepresentativeHash));
        return ((long)window.MaximumSeverity * 1_000_000) +
               (window.IsFirstOccurrence ? 100_000 : 0) + distance;
    }

    private void PruneExpiredPending(DateTimeOffset referenceUtc)
    {
        DateTimeOffset cutoff = referenceUtc - DeepDebugEvidencePolicy.ErrorWindowBefore;
        foreach (DeepDebugEvidenceFrame frame in _frames.Where(frame =>
                     !frame.Deleted && frame.TimestampUtc < cutoff &&
                     frame.WindowId is null && frame.Transitions.Count == 0))
            Delete(frame);
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
