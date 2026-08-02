using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Capture;

public sealed record CapturePlan
{
    public required PixelSize TargetSize { get; init; }

    public required int FrameCount { get; init; }

    public required TimeSpan Duration { get; init; }

    public double FramesPerSecond => Duration <= TimeSpan.Zero
        ? FrameCount
        : FrameCount / Duration.TotalSeconds;

    public void Validate()
    {
        _ = PixelSize.Create(TargetSize.Width, TargetSize.Height);
        if (FrameCount is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(FrameCount), "Frame count must be between 1 and 1000.");
        }
        if (Duration < TimeSpan.Zero || Duration > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(Duration), "Capture duration must be between 0 and 600 seconds.");
        }
        if (FrameCount > 1 && Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("A multi-frame capture requires a duration greater than zero.", nameof(Duration));
        }
    }
}
