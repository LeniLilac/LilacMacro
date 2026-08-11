using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class ScrollTrialScheduleTests
{
    [Fact]
    public void Create_AddsIncrementForEveryFollowingTrial()
    {
        IReadOnlyList<int> schedule = ScrollTrialSchedule.Create(600, 10, 10);

        Assert.Equal([600, 610, 620, 630, 640, 650, 660, 670, 680, 690], schedule);
    }

    [Fact]
    public void Create_ZeroIncrementPreservesRepeatedAbTest()
    {
        Assert.Equal([600, 600, 600], ScrollTrialSchedule.Create(600, 0, 3));
    }

    [Fact]
    public void Create_AllowsOneThousandTrials()
    {
        IReadOnlyList<int> schedule = ScrollTrialSchedule.Create(1, 10, 1000);

        Assert.Equal(1000, schedule.Count);
        Assert.Equal(9991, schedule[^1]);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(600, -1, 2)]
    [InlineData(600, 10, 0)]
    [InlineData(1, 0, 1001)]
    [InlineData(9990, 20, 2)]
    public void Create_RejectsInvalidOrOverflowingSchedules(
        int startingWheelUnits,
        int increment,
        int trialCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScrollTrialSchedule.Create(startingWheelUnits, increment, trialCount));
    }
}
