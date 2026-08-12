using LilacMacro.Core.Geometry;
using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class RobloxInputProtocolTests
{
    [Fact]
    public void Protocol_MatchesExpeditionsMacroClickTiming()
    {
        Assert.Equal(75, RobloxInputProtocol.ClickPositionSettleMilliseconds);
        Assert.Equal(20, RobloxInputProtocol.ClickHoldMilliseconds);
        Assert.Equal(4, RobloxInputProtocol.HoverClearPulseCount);
        Assert.Equal(100, RobloxInputProtocol.HoverClearPulseIntervalMilliseconds);
        Assert.Equal(100, RobloxInputProtocol.HoverRenderSettleMilliseconds);
    }

    [Fact]
    public void CameraAlignment_UsesRequestedBoundedInputs()
    {
        Assert.Equal(-5000, RobloxInputProtocol.CameraZoomWheelDelta);
        Assert.Equal(5000, RobloxInputProtocol.CameraPitchDelta);
        Assert.Equal(1000, RobloxInputProtocol.CameraMotionMilliseconds);
        Assert.Equal(50, RobloxInputProtocol.CameraInputIncrementCount);
        Assert.Equal(70, RobloxInputProtocol.ShiftLockKeyHoldMilliseconds);
    }

    [Fact]
    public void QuickPlacement_MatchesProvenExpeditionsMacroTiming()
    {
        Assert.Equal(110, RobloxInputProtocol.QuickPlacementUnitKeyHoldMilliseconds);
        Assert.Equal(250, RobloxInputProtocol.QuickPlacementUnitSelectionDelayMilliseconds);
        Assert.Equal(3, RobloxInputProtocol.QuickPlacementClickCount);
        Assert.Equal(50, RobloxInputProtocol.QuickPlacementBurstMilliseconds);
        Assert.Equal((8, 13), RobloxInputProtocol.RapidClickTiming(3, 50));
    }

    [Theory]
    [InlineData(1, 50, 50, 0)]
    [InlineData(3, 0, 0, 0)]
    [InlineData(3, 50, 8, 13)]
    public void RapidClickTiming_FitsRequestedBurst(
        int clickCount,
        int durationMilliseconds,
        int expectedHold,
        int expectedGap)
    {
        Assert.Equal(
            (expectedHold, expectedGap),
            RobloxInputProtocol.RapidClickTiming(clickCount, durationMilliseconds));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(3, -1)]
    public void RapidClickTiming_RejectsInvalidBounds(int clickCount, int durationMilliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RobloxInputProtocol.RapidClickTiming(clickCount, durationMilliseconds));
    }

    [Theory]
    [InlineData(5000, 50)]
    [InlineData(-5000, 20)]
    [InlineData(17, 4)]
    public void DistributedIncrements_PreserveExactTotal(int total, int count)
    {
        int remaining = total;
        int sum = 0;
        for (int index = 0; index < count; index++)
        {
            int increment = RobloxInputProtocol.NextDistributedIncrement(remaining, count - index);
            remaining -= increment;
            sum += increment;
        }

        Assert.Equal(total, sum);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void RegisteredMotion_NudgesAndReturnsToTarget()
    {
        Assert.Equal((1, -1), RobloxInputProtocol.RegisteredMotionDeltas(200, 1366));
        Assert.Equal((-1, 1), RobloxInputProtocol.RegisteredMotionDeltas(1365, 1366));
    }

    [Fact]
    public void ParkingPoint_MatchesExpeditionsMacroInset()
    {
        Assert.Equal(
            new PixelPoint(1341, 675),
            RobloxInputProtocol.ParkingPoint(new PixelSize(1366, 700)));
    }
}
