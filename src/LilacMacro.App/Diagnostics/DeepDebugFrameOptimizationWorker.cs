using System.Diagnostics;

namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugFrameOptimizationWorker(
    DeepDebugEvidenceRetention evidence,
    IDeepDebugFrameCodec codec,
    long maximumFrameBytes,
    TimeSpan completionDrainTimeout) : IDisposable
{
    public static readonly TimeSpan DefaultCompletionDrainTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private Task? _task;
    private long _latestObservedUtcTicks;
    private int _completionRequested;
    private int _backgroundAttempts;
    private int _completionAttempts;
    private bool _disposed;

    public void Start() => _task = Task.Run(RunAsync);

    public void Signal(DateTimeOffset observedAtUtc)
    {
        InterlockedMax(ref _latestObservedUtcTicks, observedAtUtc.UtcTicks);
        try
        {
            if (_signal.CurrentCount == 0) _signal.Release();
        }
        catch (ObjectDisposedException) { }
    }

    public async Task<DeepDebugOptimizationMetrics> CompleteAsync(DateTimeOffset completedAtUtc)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Interlocked.Exchange(ref _completionRequested, 1);
        Signal(completedAtUtc);
        bool timedOut = false;
        if (_task is not null)
        {
            try { await _task.WaitAsync(completionDrainTimeout); }
            catch (TimeoutException)
            {
                timedOut = true;
                _cancellation.Cancel();
                Signal(completedAtUtc);
                try { await _task; }
                catch (OperationCanceledException) { }
            }
        }
        stopwatch.Stop();
        return new DeepDebugOptimizationMetrics(
            Volatile.Read(ref _backgroundAttempts),
            Volatile.Read(ref _completionAttempts),
            timedOut,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_cancellation.Token);
                while (true)
                {
                    bool completing = Volatile.Read(ref _completionRequested) != 0;
                    DateTimeOffset observedAt = new(
                        Volatile.Read(ref _latestObservedUtcTicks),
                        TimeSpan.Zero);
                    bool attempted = await evidence.OptimizeNextReadyFrameAsync(
                        codec,
                        observedAt,
                        maximumFrameBytes,
                        completing,
                        _cancellation.Token);
                    if (!attempted) break;
                    if (completing) Interlocked.Increment(ref _completionAttempts);
                    else Interlocked.Increment(ref _backgroundAttempts);
                }
                if (Volatile.Read(ref _completionRequested) != 0) return;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    private static void InterlockedMax(ref long location, long candidate)
    {
        long observed = Volatile.Read(ref location);
        while (candidate > observed)
        {
            long replaced = Interlocked.CompareExchange(ref location, candidate, observed);
            if (replaced == observed) return;
            observed = replaced;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _signal.Dispose();
        _cancellation.Dispose();
    }
}
