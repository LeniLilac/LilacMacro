using LilacMacro.App.Diagnostics;
using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal sealed class MacroUnattendedRecoveryRunner(
    IDictionary<PlanTaskPrototype, DateTimeOffset> blockedUntil,
    Func<PlanTaskPrototype?> currentTask,
    Action clearCurrentTask,
    Action<string> appendLog,
    Action<PlanPrototype> refreshTasks,
    DeepDebugSessionService deepDebug,
    Action<PlanTaskPrototype?, TimeSpan> recoveryStarted)
{
    private readonly Dictionary<PlanTaskPrototype, int> _taskFailures = [];
    private readonly HashSet<PlanTaskPrototype> _indefinitelyQuarantinedUtilities = [];
    private PlanTaskPrototype? _pendingOpportunisticHandoff;

    public IReadOnlySet<PlanTaskPrototype> IndefinitelyQuarantinedUtilities =>
        _indefinitelyQuarantinedUtilities;

    public bool IsIndefinitelyQuarantined(PlanTaskPrototype task) =>
        _indefinitelyQuarantinedUtilities.Contains(task);

    public void ResetForNewRun()
    {
        _taskFailures.Clear();
        _indefinitelyQuarantinedUtilities.Clear();
        _pendingOpportunisticHandoff = null;
    }

    public PlanTaskPrototype? TakeOpportunisticHandoff()
    {
        PlanTaskPrototype? task = _pendingOpportunisticHandoff;
        _pendingOpportunisticHandoff = null;
        return task;
    }

    public void MarkTaskSucceeded(PlanTaskPrototype task)
    {
        _taskFailures.Remove(task);
        _indefinitelyQuarantinedUtilities.Remove(task);
        if (ReferenceEquals(_pendingOpportunisticHandoff, task))
            _pendingOpportunisticHandoff = null;
    }

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
                int failedTaskFailures = QuarantineWhenRequired(failedTask);
                TimeSpan delay = MacroUnattendedRecoveryPolicy.RetryDelay(consecutiveFailures);
                appendLog($"RECOVERABLE ANOMALY | {error.Message}");
                appendLog(failedTask is not null && _indefinitelyQuarantinedUtilities.Contains(failedTask)
                    ? $"RESTART + REJOIN TO CONTINUE WITH NEXT TASK IN {delay.TotalSeconds:N0}S"
                    : $"RESTART + REJOIN RETRY IN {delay.TotalSeconds:N0}S");
                deepDebug.RecordEvent("macro", "runtime_recovery", new
                {
                    Error = error.ToString(),
                    FailedTask = failedTask?.Name,
                    FailedTaskFailures = failedTaskFailures,
                    QuarantinedIndefinitely = failedTask is not null &&
                        _indefinitelyQuarantinedUtilities.Contains(failedTask),
                    RetrySeconds = delay.TotalSeconds,
                });
                refreshTasks(plan);
                recoveryStarted(failedTask, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private int QuarantineWhenRequired(PlanTaskPrototype? failedTask)
    {
        if (failedTask is null) return 0;
        if (_indefinitelyQuarantinedUtilities.Contains(failedTask))
            return MacroUnattendedRecoveryPolicy.TaskFailuresBeforeQuarantine;
        int failures = _taskFailures.GetValueOrDefault(failedTask) + 1;
        _taskFailures[failedTask] = failures;
        if (!MacroUnattendedRecoveryPolicy.ShouldQuarantineTask(failures)) return failures;

        if (MacroUnattendedRecoveryPolicy.ShouldQuarantineIndefinitely(failedTask.Mode, failures))
        {
            _indefinitelyQuarantinedUtilities.Add(failedTask);
            _taskFailures.Remove(failedTask);
            blockedUntil.Remove(failedTask);
            _pendingOpportunisticHandoff = failedTask;
            appendLog($"TASK QUARANTINED INDEFINITELY | OPPORTUNISTIC ONLY | {failedTask.Name}");
            return failures;
        }

        DateTimeOffset until = DateTimeOffset.UtcNow + MacroUnattendedRecoveryPolicy.TaskQuarantineDuration;
        blockedUntil[failedTask] = until;
        _taskFailures.Remove(failedTask);
        appendLog($"TASK QUARANTINED UNTIL {until:yyyy-MM-dd HH:mm:ss}Z | {failedTask.Name}");
        return failures;
    }
}
