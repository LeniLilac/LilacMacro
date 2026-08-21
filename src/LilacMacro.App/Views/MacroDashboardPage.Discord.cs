using LilacMacro.App.Infrastructure;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    private DiscordEventDispatcher _discordEvents = null!;
    private PlanTaskPrototype? _lastDiscordTask;

    private void InitializeDiscordEvents() =>
        _discordEvents = new DiscordEventDispatcher(
            () => _ownerState.DiscordWebhook,
            message => _ = Dispatcher.BeginInvoke(
                new Action(() => AppendLog($"DISCORD EVENT NOT SENT | {message}"))),
            _workspace.CaptureWebhookScreenshotAsync);

    private async Task BeginDiscordRunAsync(PlanPrototype plan)
    {
        _lastDiscordTask = null;
        if (_ownerState.NotifyOnRunStart)
            await NotifyDiscordAsync(DiscordEventKind.RunStarted, plan, null, "Run started.");
    }

    private async Task NotifyDiscordRunStoppedAsync(PlanPrototype plan, string detail)
    {
        if (_ownerState.NotifyOnRunStop)
            await NotifyDiscordAsync(DiscordEventKind.RunStopped, plan, _currentTask, detail);
    }

    private async Task NotifyDiscordTaskChangedAsync(PlanPrototype plan, PlanTaskPrototype task)
    {
        if (ReferenceEquals(task, _lastDiscordTask)) return;
        _lastDiscordTask = task;
        if (_ownerState.NotifyOnTaskChange)
            await NotifyDiscordAsync(
                DiscordEventKind.TaskChanged, plan, task, $"Now running priority {task.Priority}.");
    }

    private async Task NotifyDiscordOutcomeAsync(PlanPrototype plan, PlanTaskPrototype task, bool victory)
    {
        if (victory && _ownerState.NotifyOnVictory)
        {
            await NotifyDiscordAsync(
                DiscordEventKind.Victory,
                plan,
                task,
                $"Victory {_victories.GetValueOrDefault(task) + 1} of {task.Target}.");
        }
        else if (!victory && _ownerState.NotifyOnDefeat)
        {
            await NotifyDiscordAsync(
                DiscordEventKind.Defeat,
                plan,
                task,
                $"Loss {_defeats.GetValueOrDefault(task) + 1}; retry limit {task.DefeatRetries}.");
        }
    }

    private async Task NotifyDiscordRecoveryAsync(PlanTaskPrototype? task, TimeSpan delay)
    {
        if (!_ownerState.NotifyOnRecovery) return;
        PlanPrototype plan = _ownerState.SelectedPlan;
        await NotifyDiscordAsync(
            DiscordEventKind.Recovery,
            plan,
            task,
            $"Restart and rejoin retry in {delay.TotalSeconds:N0} seconds.");
    }

    private async Task NotifyDiscordTerminalFailureAsync(PlanPrototype plan)
    {
        if (!_ownerState.NotifyOnTerminalFailure) return;
        await NotifyDiscordAsync(
            DiscordEventKind.TerminalFailure,
            plan,
            _currentTask,
            "Runtime stopped before the plan could continue. Review the local run log for details.",
            _ownerState.DiscordUserId);
    }

    private Task NotifyDiscordAsync(
        DiscordEventKind kind,
        PlanPrototype plan,
        PlanTaskPrototype? task,
        string detail,
        string? mentionUserId = null) =>
        _discordEvents.CaptureAndEnqueueAsync(new DiscordEventNotification(
            kind,
            plan.Name,
            task?.Name,
            detail,
            MacroInstanceContext.Current.DisplayName,
            DateTimeOffset.UtcNow,
            mentionUserId));
}
