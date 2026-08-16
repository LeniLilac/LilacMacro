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
                new Action(() => AppendLog($"DISCORD EVENT NOT SENT | {message}"))));

    private void BeginDiscordRun(PlanPrototype plan)
    {
        _lastDiscordTask = null;
        if (_ownerState.NotifyOnRunStart)
            NotifyDiscord(DiscordEventKind.RunStarted, plan, null, "Run started.");
    }

    private void NotifyDiscordRunStopped(PlanPrototype plan, string detail)
    {
        if (_ownerState.NotifyOnRunStop)
            NotifyDiscord(DiscordEventKind.RunStopped, plan, _currentTask, detail);
    }

    private void NotifyDiscordTaskChanged(PlanPrototype plan, PlanTaskPrototype task)
    {
        if (ReferenceEquals(task, _lastDiscordTask)) return;
        _lastDiscordTask = task;
        if (_ownerState.NotifyOnTaskChange)
            NotifyDiscord(DiscordEventKind.TaskChanged, plan, task, $"Now running priority {task.Priority}.");
    }

    private void NotifyDiscordOutcome(PlanPrototype plan, PlanTaskPrototype task, bool victory)
    {
        if (victory && _ownerState.NotifyOnVictory)
        {
            NotifyDiscord(
                DiscordEventKind.Victory,
                plan,
                task,
                $"Victory {_victories.GetValueOrDefault(task) + 1} of {task.Target}.");
        }
        else if (!victory && _ownerState.NotifyOnDefeat)
        {
            NotifyDiscord(
                DiscordEventKind.Defeat,
                plan,
                task,
                $"Loss {_defeats.GetValueOrDefault(task) + 1}; retry limit {task.DefeatRetries}.");
        }
    }

    private void NotifyDiscordRecovery(PlanTaskPrototype? task, TimeSpan delay)
    {
        if (!_ownerState.NotifyOnRecovery) return;
        PlanPrototype plan = PlanCombo.SelectedItem as PlanPrototype ?? _ownerState.SelectedPlan;
        NotifyDiscord(
            DiscordEventKind.Recovery,
            plan,
            task,
            $"Restart and rejoin retry in {delay.TotalSeconds:N0} seconds.");
    }

    private void NotifyDiscordTerminalFailure(PlanPrototype plan)
    {
        if (!_ownerState.NotifyOnTerminalFailure) return;
        NotifyDiscord(
            DiscordEventKind.TerminalFailure,
            plan,
            _currentTask,
            "Runtime stopped before the plan could continue. Review the local run log for details.",
            _ownerState.DiscordUserId);
    }

    private void NotifyDiscord(
        DiscordEventKind kind,
        PlanPrototype plan,
        PlanTaskPrototype? task,
        string detail,
        string? mentionUserId = null) =>
        _discordEvents.Enqueue(new DiscordEventNotification(
            kind,
            plan.Name,
            task?.Name,
            detail,
            MacroInstanceContext.Current.DisplayName,
            DateTimeOffset.UtcNow,
            mentionUserId));
}
