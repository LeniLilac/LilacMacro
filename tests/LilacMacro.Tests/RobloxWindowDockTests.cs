using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class RobloxWindowDockTests
{
    private static readonly WindowBounds Dashboard = new(100, 100, 1700, 900);

    [Fact]
    public void StandardDockSize_IsMacroClientProfile()
    {
        Assert.Equal(1366, RobloxWindowDockService.ClientWidth);
        Assert.Equal(700, RobloxWindowDockService.ClientHeight);
    }

    [Fact]
    public void DockedStyle_RemainsTopLevelAndRemovesFrame()
    {
        const long visible = 0x10000000L;
        const long child = 0x40000000L;
        const long caption = 0x00C00000L;
        const long thickFrame = 0x00040000L;
        const long popup = 0x80000000L;

        long docked = RobloxWindowDockService.BuildDockedStyle(
            visible | child | caption | thickFrame | popup);

        Assert.NotEqual(0, docked & visible);
        Assert.NotEqual(0, docked & popup);
        Assert.Equal(0, docked & child);
        Assert.Equal(0, docked & caption);
        Assert.Equal(0, docked & thickFrame);
    }

    [Fact]
    public void DockedExtendedStyle_IsInteractiveAndTopmost()
    {
        const long unrelatedFlag = 0x00000080L;
        const long topmost = 0x00000008L;
        const long noActivate = 0x08000000L;
        const long appWindow = 0x00040000L;

        long docked = RobloxWindowDockService.BuildDockedExtendedStyle(
            unrelatedFlag | noActivate | appWindow);

        Assert.NotEqual(0, docked & unrelatedFlag);
        Assert.NotEqual(0, docked & topmost);
        Assert.Equal(0, docked & noActivate);
        Assert.Equal(0, docked & appWindow);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(202)]
    public void DashboardOrRobloxForeground_KeepsDock(int foreground)
    {
        Assert.True(IsExposed(foreground, Dashboard));
    }

    [Fact]
    public void OverlappingForeignWindow_SuspendsDock()
    {
        Assert.False(IsExposed(303, new WindowBounds(400, 250, 700, 500)));
    }

    [Fact]
    public void NonOverlappingForeignWindow_KeepsDock()
    {
        Assert.True(IsExposed(303, new WindowBounds(1850, 100, 500, 600)));
    }

    [Fact]
    public void EdgeTouchWithoutOverlap_KeepsDock()
    {
        Assert.True(IsExposed(303, new WindowBounds(1800, 100, 500, 600)));
    }

    [Fact]
    public void OwnerModal_SuspendsDock()
    {
        Assert.False(IsExposed(
            303,
            new WindowBounds(1850, 100, 500, 600),
            foregroundOwnedByOwner: true));
    }

    [Theory]
    [InlineData(false, false, RobloxDockMaintenanceAction.Acquire)]
    [InlineData(false, true, RobloxDockMaintenanceAction.Acquire)]
    [InlineData(true, true, RobloxDockMaintenanceAction.Maintain)]
    [InlineData(true, false, RobloxDockMaintenanceAction.Repair)]
    public void MaintenancePolicy_RepairsTrackedStyleDriftWithoutReacquiring(
        bool hasTrackedSource,
        bool docked,
        RobloxDockMaintenanceAction expected)
    {
        Assert.Equal(expected, RobloxDockMaintenancePolicy.Resolve(hasTrackedSource, docked));
    }

    [Fact]
    public void ForegroundRoblox_ReacquiresOnceAndRemainsExposed()
    {
        Assert.Equal(
            RobloxDockMaintenanceAction.Acquire,
            RobloxDockMaintenancePolicy.Resolve(hasTrackedSource: false, docked: false));
        Assert.True(IsExposed(202, Dashboard));

        Assert.Equal(
            RobloxDockMaintenanceAction.Maintain,
            RobloxDockMaintenancePolicy.Resolve(hasTrackedSource: true, docked: true));
        Assert.True(IsExposed(202, Dashboard));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void CoveredDashboard_ReacquiresOnlyAfterOwnerBecomesActive(
        bool awaitingOwnerExposure,
        bool ownerActive,
        bool expected) =>
        Assert.Equal(
            expected,
            RobloxDockMaintenancePolicy.CanAcquire(awaitingOwnerExposure, ownerActive));

    private static bool IsExposed(
        int foreground,
        WindowBounds foregroundBounds,
        bool foregroundOwnedByOwner = false) =>
        RobloxDockExposure.IsExposed(
            new DockExposureObservation(
                Owner: (nint)101,
                Source: (nint)202,
                Foreground: (nint)foreground,
                ForegroundVisible: true,
                ForegroundMinimized: false,
                ForegroundOwnedByOwner: foregroundOwnedByOwner,
                BoundsAvailable: true,
                OwnerBounds: Dashboard,
                ForegroundBounds: foregroundBounds));
}
