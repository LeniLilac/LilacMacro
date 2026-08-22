using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class RobloxWindowAcquisitionTests
{
    [Fact]
    public async Task Startup_reobserves_until_a_fresh_capturable_window_exists()
    {
        Queue<RobloxWindowAcquisition> observations = new(
        [
            Missing(),
            RejectedZeroArea(),
            Capturable(),
        ]);
        List<int> attempts = [];
        int delays = 0;
        RobloxWindowAcquisitionWaiter waiter = new(
            () => observations.Dequeue(),
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        RobloxWindowAcquisition result = await waiter.RunAsync(
            waitForCapturable: true,
            (attempt, _) => attempts.Add(attempt),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal([1, 2, 3], attempts);
        Assert.Equal(2, delays);
        Assert.Equal(new PixelSize(1366, 700), result.Bounds?.Size);
    }

    [Fact]
    public async Task Ordinary_refresh_is_one_fresh_observation_without_hidden_retry()
    {
        int observations = 0;
        int delays = 0;
        RobloxWindowAcquisitionWaiter waiter = new(
            () =>
            {
                observations++;
                return RejectedZeroArea();
            },
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        RobloxWindowAcquisition result = await waiter.RunAsync(
            waitForCapturable: false,
            (_, _) => { },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, observations);
        Assert.Equal(0, delays);
    }

    [Fact]
    public async Task Startup_exhausts_the_exact_attempt_bound_without_static_fallback()
    {
        int observations = 0;
        int delays = 0;
        RobloxWindowAcquisitionWaiter waiter = new(
            () =>
            {
                observations++;
                return Missing();
            },
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        RobloxWindowAcquisition result = await waiter.RunAsync(
            waitForCapturable: true,
            (_, _) => { },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RobloxWindowAcquisitionWaiter.MaximumAttempts, observations);
        Assert.Equal(RobloxWindowAcquisitionWaiter.MaximumAttempts - 1, delays);
    }

    private static RobloxWindowAcquisition Missing() => new(null, null, []);

    private static RobloxWindowAcquisition RejectedZeroArea() => new(
        null,
        null,
        [new RobloxWindowCandidateObservation(Window(), null, 0, 0, false, "zero-area")]);

    private static RobloxWindowAcquisition Capturable()
    {
        RobloxWindow window = Window();
        ClientBounds bounds = new(10, 20, 1366, 700);
        return new RobloxWindowAcquisition(
            window,
            bounds,
            [new RobloxWindowCandidateObservation(window, bounds.Size, 1366, 700, false, "capturable")]);
    }

    private static RobloxWindow Window() => new((nint)42, "Roblox", 1234, "RobloxPlayerBeta");
}
