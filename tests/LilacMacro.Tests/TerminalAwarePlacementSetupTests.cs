using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class TerminalAwarePlacementSetupTests
{
    [Fact]
    public async Task TerminalProbeConvertsPlacementFailureIntoTerminalOutcome()
    {
        List<string> statuses = [];
        int probeCount = 0;
        TerminalAwarePlacementSetup setup = new(
            (_, _, token) => Task.Delay(Timeout.InfiniteTimeSpan, token)
                .ContinueWith<MatchTerminalOutcome>(
                    _ => MatchTerminalOutcome.Victory,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.Default),
            (_, _) =>
            {
                probeCount++;
                return Task.FromResult<MatchTerminalOutcome?>(MatchTerminalOutcome.Victory);
            });

        TerminalAwarePlacementSetupResult result = await setup.RunAsync(
            new PlacementSetupExecution(),
            "cpu",
            statuses.Add,
            async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Upgrade control evidence became ambiguous.");
            },
            CancellationToken.None);

        Assert.Equal(MatchTerminalOutcome.Victory, result.TerminalOutcome);
        Assert.Equal(1, probeCount);
        Assert.Contains(
            "VICTORY DURING PLACEMENT SETUP; REMAINING STEPS SKIPPED",
            statuses);
    }

    [Fact]
    public async Task VerifiedTerminalCancelsRemainingPlacementSetup()
    {
        using CancellationTokenSource setupCanceled = new();
        TerminalAwarePlacementSetup setup = new(
            (_, _, _) => Task.FromResult(MatchTerminalOutcome.Defeat),
            (_, _) => Task.FromResult<MatchTerminalOutcome?>(null));

        TerminalAwarePlacementSetupResult result = await setup.RunAsync(
            new PlacementSetupExecution(),
            "cpu",
            null,
            async token =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return 1;
                }
                catch (OperationCanceledException)
                {
                    setupCanceled.Cancel();
                    throw;
                }
            },
            CancellationToken.None);

        Assert.Equal(MatchTerminalOutcome.Defeat, result.TerminalOutcome);
        Assert.True(setupCanceled.IsCancellationRequested);
    }
}
