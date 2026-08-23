using System.Windows;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    private readonly MacroRuntimeProgressStore _progressStore = new(
        MacroInstanceContext.Current.ConfigurationRoot,
        MacroInstanceContext.Current.Id);
    private Task? _progressLoadTask;
    private bool _progressLoaded;

    private void InitializeRuntimeProgressPersistence()
    {
        _ownerState.EnsurePlanIdentitiesPersisted();
        _ownerState.RuntimeProgressResetRequested += OwnerState_OnRuntimeProgressResetRequested;
        Loaded += MacroDashboardPage_OnLoaded;
    }

    private async void MacroDashboardPage_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        Loaded -= MacroDashboardPage_OnLoaded;
        try { await EnsureRuntimeProgressLoadedAsync(); }
        catch (Exception error) { AppToastService.ShowError("PROGRESS LOAD FAILED", error.Message); }
    }

    private async Task EnsureRuntimeProgressLoadedAsync()
    {
        if (_progressLoaded) return;
        _progressLoadTask ??= LoadRuntimeProgressAsync();
        await _progressLoadTask;
    }

    private async Task LoadRuntimeProgressAsync()
    {
        MacroRuntimeProgressSnapshot snapshot = await _progressStore.LoadAsync();
        MacroRuntimeProgressMapper.Apply(
            _ownerState.Plans,
            snapshot,
            _victories,
            _defeats,
            _completedLoopRuns,
            _utilityDueAt);
        _progressLoaded = true;
        if (PlanCombo.SelectedItem is PlanPrototype plan) RefreshUpcomingTasks(plan);
    }

    private void QueueRuntimeProgressSave()
    {
        if (!_progressLoaded) return;
        MacroRuntimeProgressSnapshot snapshot = MacroRuntimeProgressMapper.Capture(
            _ownerState.Plans,
            _victories,
            _defeats,
            _completedLoopRuns,
            _utilityDueAt);
        _ = _progressStore.QueueSave(snapshot);
    }

    private bool ReconcileCompletedLoopProgress(PlanPrototype plan)
    {
        bool advanced = MacroLoopProgressReporter.AdvanceAndReport(
            plan,
            _victories,
            _completedLoopRuns,
            _deepDebug,
            AppendLog);
        if (advanced) RefreshUpcomingTasks(plan);
        return advanced;
    }

    private void AbandonLoopIterationAfterTaskQuarantine(
        PlanPrototype plan,
        PlanTaskPrototype failedTask)
    {
        PlanLoopPrototype? loop = MacroLoopProgressPolicy.AbandonContainingIteration(
            plan,
            failedTask,
            _victories,
            _defeats,
            _completedLoopRuns);
        if (loop is null) return;

        AppendLog($"LOOP ITERATION ABANDONED | {loop.Label} | RESET AFTER {failedTask.Name}");
        _deepDebug.RecordEvent("macro", "loop_iteration_abandoned", new
        {
            Loop = loop.Label,
            FailedTask = failedTask.Name,
            Reason = "temporary_task_quarantine",
        });
        QueueRuntimeProgressSave();
    }

    private async Task FlushRuntimeProgressAsync()
    {
        if (!_progressLoaded) return;
        QueueRuntimeProgressSave();
        try { await _progressStore.FlushAsync(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppToastService.ShowError("PROGRESS SAVE FAILED", error.Message);
        }
    }

    private async void OwnerState_OnRuntimeProgressResetRequested(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (IsRunning)
        {
            AppToastService.ShowError("STOP MACRO FIRST", "Stop the Macro before resetting runtime progress.");
            return;
        }

        try
        {
            await EnsureRuntimeProgressLoadedAsync();
            MacroRuntimeProgressMapper.Apply(
                _ownerState.Plans,
                new MacroRuntimeProgressSnapshot(),
                _victories,
                _defeats,
                _completedLoopRuns,
                _utilityDueAt);
            _blockedUntil.Clear();
            _recovery.ResetForNewRun();
            _runtime.Reset();
            _runStats.Clear();
            StatsChart.SetPoints(_runStats);
            RuntimeText.Text = FormatRuntime(_runtime.Elapsed);
            if (PlanCombo.SelectedItem is PlanPrototype plan) RefreshUpcomingTasks(plan);
            _ = _progressStore.QueueSave(new MacroRuntimeProgressSnapshot());
            await FlushRuntimeProgressAsync();
            AppToastService.ShowSuccess("PROGRESS RESET", "Saved task, loop, defeat, and utility progress was cleared.");
        }
        catch (Exception error)
        {
            AppToastService.ShowError("PROGRESS RESET FAILED", error.Message);
        }
    }
}
