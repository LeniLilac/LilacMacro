using LilacMacro.Core.Capture;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class CaptureScheduleTests
{
    [Fact]
    public void Create_IncludesBothScheduleEndpoints()
    {
        CapturePlan plan = new()
        {
            TargetSize = PixelSize.Create(1280, 720),
            FrameCount = 4,
            Duration = TimeSpan.FromSeconds(3),
        };

        IReadOnlyList<TimeSpan> schedule = CaptureSchedule.Create(plan);

        Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)],
            schedule);
    }
}
