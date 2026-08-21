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
            _cancellation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            using PeriodicTimer timer = new(Interval);
            while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await capture(_cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    service.RecordEvent(
                        "diagnostic",
                        "periodic_live_frame_capture_failed",
                        new
                        {
                            IntervalMilliseconds = DeepDebugOptions.CaptureIntervalMilliseconds,
                            Error = error.ToString(),
                        });
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }
}
