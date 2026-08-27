namespace LilacMacro.App.Diagnostics;

internal sealed record DeepDebugFrameCaptureProvider(
    string Surface,
    Func<CancellationToken, Task> Capture);

internal sealed class DeepDebugFrameCaptureLoop(
    DeepDebugSessionService service,
    Func<CancellationToken, Task> capture)
{
    private readonly CancellationTokenSource _cancellation = new();
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(
        DeepDebugOptions.CaptureIntervalMilliseconds);
    private Task? _task;
    private int _consecutiveFailures;
    private DateTimeOffset? _failureStarted;
    private string? _lastFailureType;
    private string? _lastFailureMessage;

    public void Start() => _task = RunAsync();

    public async Task StopAsync()
    {
        _cancellation.Cancel();
        if (_task is null) return;
        try
        {
            await _task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            ReportFailureSummary("periodic_live_frame_capture_stopped_unavailable");
            _cancellation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            DateTimeOffset lastTick = DateTimeOffset.UtcNow;
            using PeriodicTimer timer = new(Interval);
            while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
            {
                DateTimeOffset tick = DateTimeOffset.UtcNow;
                TimeSpan observedGap = tick - lastTick;
                lastTick = tick;
                if (DeepDebugCaptureGapPolicy.ShouldReport(observedGap))
                {
                    service.RecordEvent("diagnostic", "periodic_live_frame_capture_gap", new
                    {
                        ExpectedIntervalMilliseconds = (long)Interval.TotalMilliseconds,
                        ObservedGapMilliseconds = (long)observedGap.TotalMilliseconds,
                        ObservedAtUtc = tick,
                    });
                }
                try
                {
                    await capture(_cancellation.Token).ConfigureAwait(false);
                    ReportRecovery();
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    RecordFailure(error);
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    private void RecordFailure(Exception error)
    {
        _consecutiveFailures++;
        _failureStarted ??= DateTimeOffset.UtcNow;
        _lastFailureType = error.GetType().Name;
        _lastFailureMessage = error.Message;
        if (!DeepDebugCaptureFailurePolicy.ShouldReport(_consecutiveFailures)) return;
        service.RecordEvent("diagnostic", "periodic_live_frame_capture_failed", new
        {
            IntervalMilliseconds = DeepDebugOptions.CaptureIntervalMilliseconds,
            ConsecutiveFailures = _consecutiveFailures,
            FailureType = _lastFailureType,
            Error = _consecutiveFailures == 1 ? error.ToString() : _lastFailureMessage,
        });
    }

    private void ReportRecovery()
    {
        if (_consecutiveFailures == 0) return;
        ReportFailureSummary("periodic_live_frame_capture_recovered");
        _consecutiveFailures = 0;
        _failureStarted = null;
        _lastFailureType = null;
        _lastFailureMessage = null;
    }

    private void ReportFailureSummary(string action)
    {
        if (_consecutiveFailures == 0) return;
        service.RecordEvent("diagnostic", action, new
        {
            ConsecutiveFailures = _consecutiveFailures,
            UnavailableMilliseconds = _failureStarted is { } started
                ? (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds
                : 0,
            FailureType = _lastFailureType,
            Error = _lastFailureMessage,
        });
    }
}

internal static class DeepDebugCaptureFailurePolicy
{
    internal const int SummaryInterval = 60;

    public static bool ShouldReport(int consecutiveFailures) =>
        consecutiveFailures == 1 || consecutiveFailures % SummaryInterval == 0;
}

internal static class DeepDebugCaptureGapPolicy
{
    internal static readonly TimeSpan ReportingThreshold = TimeSpan.FromSeconds(5);

    public static bool ShouldReport(TimeSpan observedGap) => observedGap >= ReportingThreshold;
}
