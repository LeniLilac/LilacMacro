namespace LilacMacro.App.Diagnostics;

public sealed record DeepDebugObservation(
    DateTimeOffset ObservedAtUtc,
    string Category,
    string Action,
    object? Data,
    byte[]? PngBytes);

public sealed partial class DeepDebugSessionService
{
    public event EventHandler<DeepDebugObservation>? ObservationRecorded;

    public event EventHandler<DeepDebugObservation>? FrameRecorded;

    private void PublishObservation(
        string category,
        string action,
        object? data,
        byte[]? pngBytes)
    {
        PublishTo(ObservationRecorded, category, action, data, pngBytes);
    }

    private void PublishFrameObservation(string source, object? data, byte[] pngBytes)
    {
        PublishTo(FrameRecorded, "frame", source, data, pngBytes);
    }

    private void PublishTo(
        EventHandler<DeepDebugObservation>? observers,
        string category,
        string action,
        object? data,
        byte[]? pngBytes)
    {
        if (observers is null) return;
        DeepDebugObservation observation = new(
            DateTimeOffset.UtcNow, category, action, data, pngBytes);
        foreach (EventHandler<DeepDebugObservation> observer in observers.GetInvocationList()
                     .Cast<EventHandler<DeepDebugObservation>>())
        {
            try { observer(this, observation); }
            catch (Exception)
            {
                // Optional diagnostic observers never own each other or the automation path.
            }
        }
    }
}
