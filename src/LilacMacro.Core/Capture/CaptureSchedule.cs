namespace LilacMacro.Core.Capture;

public static class CaptureSchedule
{
    public static IReadOnlyList<TimeSpan> Create(CapturePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();

        if (plan.FrameCount == 1) return [TimeSpan.Zero];

        long durationTicks = plan.Duration.Ticks;
        TimeSpan[] offsets = new TimeSpan[plan.FrameCount];
        for (int index = 0; index < offsets.Length; index++)
        {
            long ticks = checked(durationTicks * index / (plan.FrameCount - 1));
            offsets[index] = TimeSpan.FromTicks(ticks);
        }
        return offsets;
    }
}
