namespace LilacMacro.App.Infrastructure;

internal sealed record OcrWorkerLifecycleEvent(
    string Action,
    string Stage,
    string? Device,
    string? Model,
    long ElapsedMilliseconds);

internal sealed class OcrWorkerTimeoutException(
    string stage,
    TimeSpan timeout)
    : TimeoutException($"OCR worker {stage} did not finish within {timeout.TotalSeconds:N0} seconds.")
{
    public string Stage { get; } = stage;

    public TimeSpan Timeout { get; } = timeout;
}
