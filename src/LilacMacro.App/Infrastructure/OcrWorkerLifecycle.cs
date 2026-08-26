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

internal sealed class OcrWorkerApplicationControlException(string detail)
    : InvalidOperationException(
        "Windows Application Control blocked part of the OCR runtime. " +
        "Repair OCR in Settings after installing the latest LilacMacro update. " + detail);
