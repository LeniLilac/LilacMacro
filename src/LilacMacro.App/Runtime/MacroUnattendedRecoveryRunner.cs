using LilacMacro.App.Diagnostics;
using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal sealed class MacroUnattendedRecoveryRunner(
    IDictionary<PlanTaskPrototype, DateTimeOffset> blockedUntil,
    Func<PlanTaskPrototype?> currentTask,
    Action clearCurrentTask,
    Action<string> appendLog,
    Action<PlanPrototype> refreshTasks,
    DeepDebugSessionService deepDebug)
{
    private readonly Dictionary<PlanTaskPrototype, int> _taskFailures = [];

    public void MarkTaskSucceeded(PlanTaskPrototype task) => _taskFailures.Remove(task);

    public async Task RunAsync(
        PlanPrototype plan,
        Func<Action, CancellationToken, Task> runAttempt,
        CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        while (true)
        {
            bool madeProgress = false;
            try
            {
                await runAttempt(() => madeProgress = true, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                if (madeProgress) consecutiveFailures = 0;
                consecutiveFailures++;
                PlanTaskPrototype? failedTask = currentTask();
                clearCurrentTask();
                QuarantineWhenRequired(failedTask);
                TimeSpan delay = MacroUnattendedRecoveryPolicy.RetryDelay(consecutiveFailures);
                appendLog($"RECOVERABLE ANOMALY | {error.Message}");
                appendLog($"RESTART + REJOIN RETRY IN {delay.TotalSeconds:N0}S");
                deepDebug.RecordEvent("macro", "runtime_recovery", new
                {
                    Error = error.ToString(),
                    FailedTask = failedTask?.Name,
                    RetrySeconds = delay.TotalSeconds,
                });
                refreshTasks(plan);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private void QuarantineWhenRequired(PlanTaskPrototype? failedTask)
    {
        if (failedTask is null) return;
        int failures = _taskFailures.GetValueOrDefault(failedTask) + 1;
        _taskFailures[failedTask] = failures;
        if (!MacroUnattendedRecoveryPolicy.ShouldQuarantineTask(failures)) return;

        DateTimeOffset until = DateTimeOffset.UtcNow + MacroUnattendedRecoveryPolicy.TaskQuarantineDuration;
        blockedUntil[failedTask] = until;
        _taskFailures.Remove(failedTask);
        appendLog($"TASK QUARANTINED UNTIL {until:yyyy-MM-dd HH:mm:ss}Z | {failedTask.Name}");
    }
}
