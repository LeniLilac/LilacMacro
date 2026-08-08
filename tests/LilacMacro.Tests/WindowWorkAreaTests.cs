using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class WindowWorkAreaTests
{
    [Fact]
    public void InitialWorkspace_Fills1920MonitorWorkAreaWithoutUsingMaximizedState()
    {
        DesktopWorkAreaBounds fitted = WindowsWindowWorkArea.FitNormalBounds(
            new DesktopWorkAreaBounds(200, 100, 1800, 1000),
            new DesktopWorkAreaBounds(0, 0, 1920, 1040),
            desiredWidth: 1920,
            desiredHeight: 1080);

        Assert.Equal(new DesktopWorkAreaBounds(0, 0, 1920, 1040), fitted);
    }

    [Fact]
    public void InitialWorkspace_StaysInsideOffsetMonitorWorkArea()
    {
        DesktopWorkAreaBounds fitted = WindowsWindowWorkArea.FitNormalBounds(
            new DesktopWorkAreaBounds(2200, 100, 1200, 800),
            new DesktopWorkAreaBounds(1920, 40, 2560, 1400),
            desiredWidth: 1920,
            desiredHeight: 1080);

        Assert.Equal(new DesktopWorkAreaBounds(1920, 40, 1920, 1080), fitted);
    }
}
