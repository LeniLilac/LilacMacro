using System.Diagnostics;
using LilacMacro.App.Debugging;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class PlacementSetupExecution
{
    public int ExecutedSteps { get; set; }

    public void CountStep() => ExecutedSteps++;
}

internal sealed record TerminalAwarePlacementSetupResult(
    int ExecutedSteps,
    MatchTerminalOutcome? TerminalOutcome);

internal sealed class TerminalAwarePlacementSetup
{
    private readonly Func<string, Action<string>?, CancellationToken, Task<MatchTerminalOutcome>> _waitUntilTerminal;
    private readonly Func<string, CancellationToken, Task<MatchTerminalOutcome?>> _tryObserve;

    public TerminalAwarePlacementSetup(MatchTerminalService terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        _waitUntilTerminal = terminal.WaitUntilTerminalAsync;
        _tryObserve = terminal.TryObserveAsync;
    }

    internal TerminalAwarePlacementSetup(
        Func<string, Action<string>?, CancellationToken, Task<MatchTerminalOutcome>> waitUntilTerminal,
        Func<string, CancellationToken, Task<MatchTerminalOutcome?>> tryObserve)
    {
        ArgumentNullException.ThrowIfNull(waitUntilTerminal);
        ArgumentNullException.ThrowIfNull(tryObserve);
        _waitUntilTerminal = waitUntilTerminal;
        _tryObserve = tryObserve;
    }

    public async Task SatisfyStartBoundaryAsync(
        string device,
        Action<string>? status,
        Func<CancellationToken, Task<DebugRunReport>> startGame,
        CancellationToken cancellationToken)
    {
        Stopwatch retryWindow = Stopwatch.StartNew();
        int attempts = 0;
        while (MatchLoadPolicy.IsWithinRetryWindow(retryWindow.Elapsed))
        {
            attempts++;
            DebugRunReport start = await startGame(cancellationToken);
            if (start.Succeeded) return;
            status?.Invoke(
                $"START SCREEN ABSENT AFTER {attempts} FRESH OBSERVATION(S); " +
                $"RETRYING UNTIL {MatchLoadPolicy.RetryWindow.TotalSeconds:0}S");
            TimeSpan delay = MatchLoadPolicy.RetryDelay(retryWindow.Elapsed);
            if (delay <= TimeSpan.Zero) break;
            await Task.Delay(delay, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Start Game did not expose a verified action within " +
            $"{MatchLoadPolicy.RetryWindow.TotalSeconds:0} seconds after " +
            $"{attempts} fresh observation(s).");
    }

    public async Task<TerminalAwarePlacementSetupResult> RunAsync(
        PlacementSetupExecution execution,
        string device,
        Action<string>? status,
        Func<CancellationToken, Task<int>> setup,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource setupCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource terminalCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<int> setupTask = setup(setupCancellation.Token);
        Task<MatchTerminalOutcome> terminalTask = _waitUntilTerminal(
            device, status, terminalCancellation.Token);
        try
        {
            Task completed = await Task.WhenAny(setupTask, terminalTask).ConfigureAwait(false);

            if (ReferenceEquals(completed, terminalTask))
            {
                MatchTerminalOutcome outcome = await terminalTask.ConfigureAwait(false);
                setupCancellation.Cancel();
                await FinishSetupAfterTerminalAsync(setupTask, cancellationToken, status).ConfigureAwait(false);
                return TerminalResult(outcome, execution, status);
            }

            if (setupTask.IsFaulted || setupTask.IsCanceled)
            {
                TerminalAwarePlacementSetupResult? recovered =
                    await TryRecoverTerminalAfterSetupFailureAsync(
                        terminalTask,
                        terminalCancellation,
                        execution,
                        device,
                        status,
                        cancellationToken).ConfigureAwait(false);
                if (recovered is not null) return recovered;
            }

            await setupTask.ConfigureAwait(false);
            if (terminalTask.IsCompletedSuccessfully)
            {
                return new TerminalAwarePlacementSetupResult(
                    execution.ExecutedSteps, terminalTask.Result);
            }

            terminalCancellation.Cancel();
            await IgnoreMonitorCancellationAsync(terminalTask).ConfigureAwait(false);
            return new TerminalAwarePlacementSetupResult(execution.ExecutedSteps, null);
        }
        catch
        {
            setupCancellation.Cancel();
            terminalCancellation.Cancel();
            await IgnoreTaskAsync(setupTask).ConfigureAwait(false);
            await IgnoreTaskAsync(terminalTask).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<TerminalAwarePlacementSetupResult?> TryRecoverTerminalAfterSetupFailureAsync(
        Task<MatchTerminalOutcome> terminalTask,
        CancellationTokenSource terminalCancellation,
        PlacementSetupExecution execution,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        if (terminalTask.IsCompletedSuccessfully)
            return TerminalResult(terminalTask.Result, execution, status);

        terminalCancellation.Cancel();
        await IgnoreTaskAsync(terminalTask).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return null;

        try
        {
            MatchTerminalOutcome? outcome = await _tryObserve(device, cancellationToken)
                .ConfigureAwait(false);
            return outcome is MatchTerminalOutcome terminal
                ? TerminalResult(terminal, execution, status)
                : null;
        }
        catch (Exception error) when (
            !cancellationToken.IsCancellationRequested && IsRecoverableObservationFailure(error))
        {
            status?.Invoke($"TERMINAL PROBE AFTER PLACEMENT FAILURE RETRY | {error.Message}");
            return null;
        }
    }

    private static TerminalAwarePlacementSetupResult TerminalResult(
        MatchTerminalOutcome outcome,
        PlacementSetupExecution execution,
        Action<string>? status)
    {
        status?.Invoke(
            $"{outcome.ToString().ToUpperInvariant()} DURING PLACEMENT SETUP; " +
            "REMAINING STEPS SKIPPED");
        return new TerminalAwarePlacementSetupResult(execution.ExecutedSteps, outcome);
    }

    private static bool IsRecoverableObservationFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or
        InvalidOperationException or TimeoutException;

    private static async Task FinishSetupAfterTerminalAsync(
        Task<int> setupTask,
        CancellationToken cancellationToken,
        Action<string>? status)
    {
        try
        {
            await setupTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            status?.Invoke($"PLACEMENT SETUP STOPPED AFTER TERMINAL | {error.Message}");
        }
    }

    private static async Task IgnoreMonitorCancellationAsync(Task monitor)
    {
        try
        {
            await monitor.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task IgnoreTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
