using LilacMacro.App.Runtime;

namespace LilacMacro.Tests;

public sealed class MacroInternetConnectivityGateTests
{
    [Fact]
    public async Task Available_connection_returns_without_logging_or_waiting()
    {
        List<string> logs = [];
        int checks = 0;
        int waits = 0;
        MacroInternetConnectivityGate gate = new(
            () =>
            {
                checks++;
                return true;
            },
            logs.Add,
            TimeSpan.FromSeconds(1),
            (_, _) =>
            {
                waits++;
                return Task.CompletedTask;
            });

        await gate.WaitUntilAvailableAsync(CancellationToken.None);

        Assert.Equal(1, checks);
        Assert.Equal(0, waits);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Offline_connection_waits_until_restored_and_logs_each_transition_once()
    {
        List<string> logs = [];
        bool available = false;
        int waits = 0;
        MacroInternetConnectivityGate gate = new(
            () => available,
            logs.Add,
            TimeSpan.FromSeconds(1),
            (_, _) =>
            {
                waits++;
                available = true;
                return Task.CompletedTask;
            });

        await gate.WaitUntilAvailableAsync(CancellationToken.None);

        Assert.Equal(1, waits);
        Assert.Equal(
            [
                "WAITING FOR INTERNET | RECOVERY PAUSED",
                "INTERNET RESTORED | RECOVERY RESUMING",
            ],
            logs);
    }

    [Fact]
    public async Task Offline_connection_wait_honors_cancellation()
    {
        List<string> logs = [];
        using CancellationTokenSource cancellation = new();
        MacroInternetConnectivityGate gate = new(
            () => false,
            logs.Add,
            TimeSpan.FromSeconds(1),
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.WaitUntilAvailableAsync(cancellation.Token));

        Assert.Equal(["WAITING FOR INTERNET | RECOVERY PAUSED"], logs);
    }
}
