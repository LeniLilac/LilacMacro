using LilacMacro.Core.Runtime;
using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class WindowsRobloxDisplayScaleTests
{
    [Theory]
    [InlineData(96u, 100)]
    [InlineData(120u, 125)]
    [InlineData(144u, 150)]
    [InlineData(192u, 200)]
    [InlineData(106u, 110)]
    public void PercentageUsesEffectiveMonitorDpi(uint dpi, int expectedPercentage) =>
        Assert.Equal(expectedPercentage, WindowsRobloxDisplayScale.ScalePercentageFromDpi(dpi));

    [Fact]
    public void FailureExplainsTheRequiredUserAction()
    {
        RobloxDisplayScaleException error = new(125);

        Assert.Equal(125, error.ScalePercentage);
        Assert.Equal(
            "Roblox is on a monitor using 125% Windows display scale. " +
            "Change that monitor to 100%, then restart Roblox and retry.",
            error.Message);
    }
}
