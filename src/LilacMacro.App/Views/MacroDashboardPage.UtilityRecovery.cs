using LilacMacro.App.Runtime;
using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    private string SelectOcrDevice()
    {
        if (_ocr.IsDeviceReady(OcrRunner.GpuDevice)) return OcrRunner.GpuDevice;
        if (_ocr.IsDeviceReady(OcrRunner.CpuDevice)) return OcrRunner.CpuDevice;
        throw new InvalidOperationException("Automatic OCR setup did not complete. Retry OCR setup before starting the macro.");
    }

    private DateTimeOffset EligibleAt(PlanTaskPrototype task, DateTimeOffset fallback)
    {
        DateTimeOffset eligible = _blockedUntil.GetValueOrDefault(task, fallback);
        if (_utilityDueAt.TryGetValue(task, out DateTimeOffset utilityDue) && utilityDue > eligible)
            eligible = utilityDue;
        return eligible;
    }

    private PlanTaskPrototype? SelectEligibleTask(PlanPrototype plan, DateTimeOffset observedAt) =>
        MacroPriorityPolicy.SelectEligibleAt(
            plan,
            _victories,
            _completedLoopRuns,
            observedAt,
            EligibleAt,
            IsTaskEnabledForSelection);

    private bool IsTaskEnabledForSelection(PlanTaskPrototype task, DateTimeOffset observedAt) =>
        !_recovery.IsIndefinitelyQuarantined(task) &&
        _control.IsTaskEnabled(task, observedAt);

    private async Task RunLobbyHandoffOpportunityAsync(
        PlanPrototype plan,
        PlanTaskPrototype previousTask,
        PlanTaskPrototype nextTask,
        string device,
        Action madeProgress,
        CancellationToken cancellationToken)
    {
        if (!MacroUnattendedRecoveryPolicy.CanAttemptOpportunistically(
                PlanTaskMode.Utilities,
                taskSwitchAvailable: !ReferenceEquals(previousTask, nextTask),
                attemptsAtBoundary: 0))
            return;
        await RunOpportunisticUtilityAsync(plan, device, madeProgress, cancellationToken);
    }

    private async Task RunOpportunisticUtilityAsync(
        PlanPrototype plan,
        string device,
        Action madeProgress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        PlanTaskPrototype? task = MacroPriorityPolicy.SelectOpportunisticUtilityAt(
            plan,
            _victories,
            _completedLoopRuns,
            _recovery.IndefinitelyQuarantinedUtilities,
            observedAt,
            _control.IsTaskEnabled);
        if (task is null) return;

        _currentTask = task;
        await NotifyDiscordTaskChangedAsync(plan, task);
        RefreshUpcomingTasks(plan);
        AppendLog($"OPPORTUNISTIC UTILITY RETRY | {task.Name}");
        MacroRuntimeKeySnapshot keys = _ownerState.KeyBindings.Snapshot();
        await _utilities.RunAsync(
            task.Route,
            task.ShopItemIds,
            keys.AreasMenu,
            keys.MacroToggle,
            device,
            AppendLog,
            cancellationToken);
        _utilityDueAt[task] = _control.NextUtilityDue(task, DateTimeOffset.UtcNow);
        _recovery.MarkTaskSucceeded(task);
        madeProgress();
        QueueRuntimeProgressSave();
        AppendLog($"OPPORTUNISTIC UTILITY COMPLETE | NEXT {_utilityDueAt[task]:yyyy-MM-dd HH:mm:ss}Z");
        _currentTask = null;
        RefreshUpcomingTasks(plan);
    }
}
