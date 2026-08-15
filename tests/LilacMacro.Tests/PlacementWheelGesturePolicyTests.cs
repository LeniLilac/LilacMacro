using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class PlacementWheelGesturePolicyTests
{
    [Fact]
    public void BurstThatStartsOutsideMapNeverTransfersToMap()
    {
        PlacementWheelGesturePolicy policy = new(TimeSpan.FromMilliseconds(320));
        DateTimeOffset start = DateTimeOffset.UtcNow;

        Assert.False(policy.Observe(start, pointerOverMap: false));
        Assert.False(policy.Observe(start.AddMilliseconds(120), pointerOverMap: true));
    }

    [Fact]
    public void NewBurstInsideMapOwnsZoom()
    {
        PlacementWheelGesturePolicy policy = new(TimeSpan.FromMilliseconds(320));
        DateTimeOffset start = DateTimeOffset.UtcNow;

        Assert.False(policy.Observe(start, pointerOverMap: false));
        Assert.True(policy.Observe(start.AddMilliseconds(400), pointerOverMap: true));
        Assert.True(policy.Observe(start.AddMilliseconds(500), pointerOverMap: true));
    }
}
