namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugEvidenceRetention
{
    private static readonly TimeSpan RawFrameWindow = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly List<DeepDebugEvidenceFrame> _frames = [];
    private readonly List<DeepDebugEvidenceWindow> _windows = [];
    private readonly List<string> _pendingTransitions = [];
    private int _nextWindowId;
    private bool _optimized;

    public int RetainedFrameCount { get { lock (_gate) return _frames.Count(frame => !frame.Deleted); } }
    public int DiscardedFrameCount { get; private set; }
    public int WindowCount { get { lock (_gate) return _windows.Count; } }
    public int DiscardedWindowCount { get; private set; }
    public int TransitionFrameCount
    {
        get { lock (_gate) return _frames.Count(frame => !frame.Deleted && frame.Transitions.Count > 0); }
    }
    public long RetainedBytes { get { lock (_gate) return RetainedBytesNoLock(); } }
    public bool IsOptimized => _optimized;
    public int AvifFrameCount
    {
        get { lock (_gate) return _frames.Count(frame => !frame.Deleted && frame.Format == "avif"); }
    }
    public int JpegFrameCount
    {
        get { lock (_gate) return _frames.Count(frame => !frame.Deleted && frame.Format == "jpeg"); }
    }
    public int LossyFrameCount
    {
        get { lock (_gate) return _frames.Count(frame => !frame.Deleted && frame.EncodingMode == "lossy"); }
    }
    public IReadOnlyList<DeepDebugEvidenceFrame> Frames
    {
        get { lock (_gate) return _frames.ToArray(); }
    }

    public void ObserveEvent(string category, string action, object? data, DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            if (DeepDebugEvidencePolicy.TryClassifyError(
                    category, action, data, timestampUtc, out DeepDebugErrorMarker? error))
                BeginErrorWindow(error!);
            if (DeepDebugEvidencePolicy.TransitionSignature(category, action) is { } transition)
                MarkTransition(transition, timestampUtc);
        }
    }

    public void RecordFrame(string path, DateTimeOffset timestampUtc, byte[] png, bool fullClient)
    {
        lock (_gate)
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
                active.Add(frame);
            if (_pendingTransitions.Count <= 0) return;
            foreach (string transition in _pendingTransitions) frame.Transitions.Add(transition);
            _pendingTransitions.Clear();
        }
    }

    public async Task CompleteAsync(IDeepDebugFrameCodec codec, long maximumFrameBytes)
    {
        DateTimeOffset newest;
        lock (_gate)
        {
            if (_frames.Count == 0) return;
            newest = _frames.Max(frame => frame.TimestampUtc);
        }
        while (await OptimizeNextReadyFrameAsync(
                   codec, newest, maximumFrameBytes, finalizing: true, CancellationToken.None)) { }
        Complete(maximumFrameBytes);
    }

    internal void Complete(long maximumFrameBytes)
    {
        lock (_gate)
        {
            if (RetainedBytesNoLock() > maximumFrameBytes) TrimToBudget(maximumFrameBytes);
        }
    }

    internal void OptimizeWhenAbove(long maximumFrameBytes) => Complete(maximumFrameBytes);

    internal async Task<bool> OptimizeNextReadyFrameAsync(
        IDeepDebugFrameCodec codec,
        DateTimeOffset newestTimestampUtc,
        long maximumFrameBytes,
        bool finalizing,
        CancellationToken cancellationToken)
    {
        DeepDebugEvidenceFrame? frame;
        bool lossless;
        lock (_gate)
        {
            DateTimeOffset cutoff = newestTimestampUtc - RawFrameWindow;
            frame = _frames.FirstOrDefault(candidate =>
                !candidate.Deleted && !candidate.EncodingAttempted && !candidate.EncodingInProgress &&
                candidate.Format == "png" && candidate.IsImportant &&
                (finalizing || candidate.TimestampUtc < cutoff));
            lossless = frame is not null;
            frame ??= _frames.FirstOrDefault(candidate =>
                !candidate.Deleted && !candidate.EncodingAttempted && !candidate.EncodingInProgress &&
                candidate.Format == "png" && !candidate.IsImportant && candidate.TimestampUtc < cutoff);
            if (frame is null)
            {
                if (RetainedBytesNoLock() > maximumFrameBytes) TrimToBudget(maximumFrameBytes);
                return false;
            }
            frame.EncodingInProgress = true;
            frame.EncodingAttempted = true;
        }

        await EncodeReservedAsync(frame, codec, lossless, finalizing, cancellationToken);
        return true;
    }

    public bool DropLowestPriorityEvidence()
    {
        lock (_gate) return DropLowestPriorityEvidenceNoLock();
    }

    public long DropLowestPriorityEvidence(long minimumBytes)
    {
        lock (_gate)
        {
            long before = RetainedBytesNoLock();
            while (before - RetainedBytesNoLock() < minimumBytes && DropLowestPriorityEvidenceNoLock()) { }
            long discarded = before - RetainedBytesNoLock();
            if (discarded > 0) _optimized = true;
            return discarded;
        }
    }

    private async Task EncodeReservedAsync(
        DeepDebugEvidenceFrame frame,
        IDeepDebugFrameCodec codec,
        bool lossless,
        bool waitForLease,
        CancellationToken cancellationToken)
    {
        string originalPath = frame.Path;
        bool replacementCompleted = false;
        DeepDebugFrameEncodingResult result;
        try
        {
            result = await codec.EncodeAsync(frame.Path, lossless, waitForLease, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                frame.Validation = "encoder-canceled";
                frame.EncodingInProgress = false;
            }
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
                                      System.ComponentModel.Win32Exception)
        {
            lock (_gate)
            {
                frame.Validation = error is OperationCanceledException ? "encoder-canceled" : "encoder-exception";
                frame.EncodingInProgress = false;
            }
            return;
        }

        string encodedPath = Path.ChangeExtension(frame.Path, "." + result.Format);
        string temporary = encodedPath + ".tmp";
        try
        {
            if (result.Success && result.Bytes is not null)
                await File.WriteAllBytesAsync(temporary, result.Bytes, cancellationToken);
            lock (_gate)
            {
                frame.Validation = result.Validation;
                frame.Quality = result.Quality;
                if (result.Validation == "encoder-busy") frame.EncodingAttempted = false;
                if (!result.Success || result.Bytes is null) return;
                if (!lossless && frame.IsImportant)
                {
                    frame.EncodingAttempted = false;
                    frame.Validation = "importance-upgraded-before-replacement";
                    return;
                }
                File.Move(temporary, encodedPath, overwrite: false);
                File.Delete(frame.Path);
                frame.Path = encodedPath;
                frame.ArtifactPath = Path.ChangeExtension(frame.ArtifactPath, "." + result.Format)
                    .Replace('\\', '/');
                frame.Length = result.Bytes.LongLength;
                frame.Format = result.Format;
                frame.EncodingMode = lossless ? "lossless" : "lossy";
                replacementCompleted = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate) frame.Validation = "encoder-canceled";
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
                frame.Validation = error is OperationCanceledException ? "encoder-canceled" : "replacement-failed";
        }
        finally
        {
            lock (_gate) frame.EncodingInProgress = false;
            TryDelete(temporary);
            if (!replacementCompleted && File.Exists(originalPath)) TryDelete(encodedPath);
        }
    }

    private void TrimToBudget(long maximumBytes)
    {
        _optimized = true;
        while (RetainedBytesNoLock() > maximumBytes && DropLowestPriorityEvidenceNoLock()) { }
    }

    private bool DropLowestPriorityEvidenceNoLock()
    {
        foreach (DeepDebugEvidenceFrame ordinary in _frames
            .Where(frame => !frame.Deleted && !frame.EncodingInProgress &&
                            frame.WindowId is null && frame.Transitions.Count == 0)
            .OrderBy(frame => frame.EncodingMode == "lossy" ? 0 : 1)
            .ThenBy(frame => frame.TimestampUtc))
        {
            if (Delete(ordinary)) return true;
        }

        foreach (DeepDebugEvidenceFrame transition in _frames
            .Where(frame => !frame.Deleted && !frame.EncodingInProgress && frame.WindowId is null)
            .OrderBy(frame => frame.TimestampUtc))
        {
            if (Delete(transition)) return true;
        }

        DeepDebugEvidenceWindow? window = _windows
            .Where(candidate => candidate.Frames.Any(frame => !frame.Deleted && !frame.EncodingInProgress))
            .OrderBy(candidate => candidate.MaximumSeverity)
            .ThenBy(candidate => candidate.IsFirstOccurrence)
            .ThenBy(RedundancyDistance)
            .ThenBy(candidate => candidate.StartUtc)
            .FirstOrDefault();
        if (window is null) return false;
        DeepDebugEvidenceFrame? leastUsefulFrame = window.Frames
            .Where(frame => !frame.Deleted && !frame.EncodingInProgress)
            .OrderByDescending(frame => window.Errors.Min(error =>
                Math.Abs((frame.TimestampUtc - error.TimestampUtc).Ticks)))
            .ThenBy(frame => frame.TimestampUtc)
            .FirstOrDefault();
        if (leastUsefulFrame is null || !Delete(leastUsefulFrame)) return false;
        if (!window.Frames.Any(frame => !frame.Deleted)) DiscardedWindowCount++;
        return true;
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

    private void MarkTransition(string signature, DateTimeOffset timestampUtc)
    {
        DateTimeOffset oldestEligible = timestampUtc - RawFrameWindow;
        DeepDebugEvidenceFrame? frame = _frames
            .Where(candidate => !candidate.Deleted && candidate.FullClient &&
                                candidate.Format == "png" && !candidate.EncodingAttempted &&
                                candidate.TimestampUtc >= oldestEligible)
            .OrderByDescending(candidate => candidate.TimestampUtc)
            .FirstOrDefault() ?? _frames
            .Where(candidate => !candidate.Deleted && candidate.Format == "png" &&
                                !candidate.EncodingAttempted && candidate.TimestampUtc >= oldestEligible)
            .OrderByDescending(candidate => candidate.TimestampUtc)
            .FirstOrDefault();
        if (frame is null) _pendingTransitions.Add(signature);
        else frame.Transitions.Add(signature);
    }

    private int RedundancyDistance(DeepDebugEvidenceWindow candidate) => _windows
        .Where(window => window.Id != candidate.Id && window.Frames.Any(frame => !frame.Deleted))
        .Select(window => DeepDebugPerceptualHash.Distance(candidate.RepresentativeHash, window.RepresentativeHash))
        .DefaultIfEmpty(64)
        .Min();

    private bool Delete(DeepDebugEvidenceFrame frame)
    {
        if (frame.Deleted || frame.EncodingInProgress) return false;
        try
        {
            if (File.Exists(frame.Path)) File.Delete(frame.Path);
            frame.Deleted = true;
            DiscardedFrameCount++;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private long RetainedBytesNoLock() => _frames.Where(frame => !frame.Deleted).Sum(frame => frame.Length);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
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
    public string Path { get; set; } = path;
    public string OriginalArtifactPath { get; } = $"frames/{System.IO.Path.GetFileName(path)}";
    public string ArtifactPath { get; set; } = $"frames/{System.IO.Path.GetFileName(path)}";
    public DateTimeOffset TimestampUtc { get; } = timestampUtc;
    public long OriginalLength { get; } = length;
    public long Length { get; set; } = length;
    public ulong PerceptualHash { get; } = perceptualHash;
    public bool FullClient { get; } = fullClient;
    public int? WindowId { get; set; }
    public HashSet<string> Transitions { get; } = new(StringComparer.Ordinal);
    public bool Deleted { get; set; }
    public bool EncodingAttempted { get; set; }
    public bool EncodingInProgress { get; set; }
    public string Format { get; set; } = "png";
    public string EncodingMode { get; set; } = "none";
    public int? Quality { get; set; }
    public string Validation { get; set; } = "not-attempted";
    public bool IsImportant => WindowId is not null || Transitions.Count > 0;
}
