using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class RobloxClientVisibilityPolicyTests
{
    private static readonly ScreenWorkArea WorkArea = new(0, 0, 1920, 1150);

    [Fact]
    public void FullyVisibleClient_DoesNotMoveWindow()
    {
        WindowBounds window = new(92, 69, 1382, 739);
        WindowBounds fitted = RobloxClientVisibilityPolicy.FitWindow(
            new ClientBounds(100, 100, 1366, 700), window, WorkArea);
        Assert.Equal(window, fitted);
    }

    [Fact]
    public void ClientBehindBottomTaskbar_MovesUpByExactOverflow()
    {
        WindowBounds fitted = RobloxClientVisibilityPolicy.FitWindow(
            new ClientBounds(392, 560, 1366, 700),
            new WindowBounds(384, 529, 1382, 739),
            WorkArea);
        Assert.Equal(new WindowBounds(384, 419, 1382, 739), fitted);
    }

    [Fact]
    public void ClientBeyondRightEdge_MovesLeftByExactOverflow()
    {
        WindowBounds fitted = RobloxClientVisibilityPolicy.FitWindow(
            new ClientBounds(700, 100, 1366, 700),
            new WindowBounds(692, 69, 1382, 739),
            WorkArea);
        Assert.Equal(new WindowBounds(546, 69, 1382, 739), fitted);
    }

    [Fact]
    public void ClientAboveTopEdge_MovesDownByExactOverflow()
    {
        WindowBounds fitted = RobloxClientVisibilityPolicy.FitWindow(
            new ClientBounds(100, -100, 1366, 700),
            new WindowBounds(92, -131, 1382, 739),
            WorkArea);
        Assert.Equal(new WindowBounds(92, -31, 1382, 739), fitted);
    }

    [Fact]
    public void ClientOutsideLeftOffsetMonitor_MovesInsideThatMonitor()
    {
        ScreenWorkArea workArea = new(-1920, 40, 0, 1080);
        WindowBounds fitted = RobloxClientVisibilityPolicy.FitWindow(
            new ClientBounds(-2000, 100, 1366, 700),
            new WindowBounds(-2008, 69, 1382, 739),
            workArea);
        Assert.Equal(new WindowBounds(-1928, 69, 1382, 739), fitted);
    }

    [Fact]
    public void ClientExactlyMatchingWorkWidth_CanPlaceBordersOutsideWorkArea()
    {
        ScreenWorkArea workArea = new(0, 0, 1366, 728);
        WindowBounds fitted = RobloxClientVisibilityPolicy.FitWindow(
            new ClientBounds(208, 100, 1366, 700),
            new WindowBounds(200, 69, 1382, 739),
            workArea);
        Assert.Equal(new WindowBounds(-8, -3, 1382, 739), fitted);
    }

    [Fact]
    public void ClientLargerThanWorkArea_FailsBeforeInput()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            RobloxClientVisibilityPolicy.FitWindow(
                new ClientBounds(0, 0, 1366, 700),
                new WindowBounds(-8, -31, 1382, 739),
                new ScreenWorkArea(0, 0, 1280, 720)));
        Assert.Contains("does not fit", error.Message, StringComparison.Ordinal);
    }
}
