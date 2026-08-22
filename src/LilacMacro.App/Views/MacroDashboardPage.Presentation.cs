using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    private void StopButton_OnClick(object sender, RoutedEventArgs eventArgs) => _runCancellation?.Cancel();

    private void DockButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        RobloxDock.SetRequested(!RobloxDock.IsRequested);
        RefreshDockState();
    }

    private void RobloxDock_OnStateChanged(object? sender, EventArgs eventArgs) => RefreshDockState();

    private void RefreshDockState()
    {
        if (DockStatusText is null || DockButtonText is null) return;
        DockStatusText.Text = RobloxDock.Status;
        DockButtonText.Text = RobloxDock.IsRequested ? "UNDOCK" : "DOCK";
        DockButton.SetResourceReference(
            Control.BackgroundProperty,
            RobloxDock.IsRequested ? "AccentBrush" : "CardBrush");
    }

    private void RefreshRunState(bool running)
    {
        StartButton.IsEnabled = !running &&
            !_runStarting &&
            !_ocrSetupInProgress &&
            MacroPlanPreflight.HasTasks(PlanCombo.SelectedItem as PlanPrototype);
        StopButton.IsEnabled = running;
        PlanCombo.IsEnabled = !running;
        StartButtonText.Text = _ocrReady
            ? "START"
            : _ocrSetupInProgress
                ? "SETTING UP OCR"
                : _ocrSetupFailed ? "RETRY OCR SETUP" : "SET UP OCR";
        RuntimeText.Text = FormatRuntime(_runtime.Elapsed);
        RunningChanged?.Invoke(running);
    }

    private static string FormatRuntime(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

    private void UpdateStartButtonState()
    {
        StartButton.IsEnabled = !_runStarting &&
            _runTask is null &&
            !_ocrSetupInProgress &&
            MacroPlanPreflight.HasTasks(PlanCombo.SelectedItem as PlanPrototype);
        StartButtonText.Text = _ocrReady
            ? "START"
            : _ocrSetupInProgress
                ? "SETTING UP OCR"
                : _ocrSetupFailed ? "RETRY OCR SETUP" : "SET UP OCR";
    }

    private bool CanStartPlan(PlanPrototype plan)
    {
        if (MacroPlanPreflight.HasTasks(plan)) return true;
        AppToastService.ShowError("PLAN HAS NO TASKS", "Add at least one task before starting the Macro.");
        return false;
    }

    private void PlanCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        UpdateStartButtonState();
        if (PlanCombo.SelectedItem is not PlanPrototype plan || UpcomingTasksList is null) return;
        _ownerState.SelectPlan(plan);
        _currentTask = null;
        RefreshUpcomingTasks(plan);
    }

    private void OwnerState_OnSelectedPlanChanged(object? sender, EventArgs eventArgs)
    {
        if (!ReferenceEquals(PlanCombo.SelectedItem, _ownerState.SelectedPlan))
            PlanCombo.SelectedItem = _ownerState.SelectedPlan;
    }

    private void OwnerState_OnPlansChanged(object? sender, EventArgs eventArgs)
    {
        if (PlanCombo.SelectedItem is not PlanPrototype plan) return;
        PlanCombo.Items.Refresh();
        PlanCombo.SelectedItem = null;
        PlanCombo.SelectedItem = plan;
    }

    private void RefreshUpcomingTasks(PlanPrototype plan) =>
        UpcomingTasksList.ItemsSource = UpcomingTaskRowFactory.Build(
            plan,
            _currentTask,
            _victories,
            _completedLoopRuns,
            DateTimeOffset.UtcNow,
            EligibleAt,
            _recovery.IsIndefinitelyQuarantined);

    private void AppendLog(string message)
    {
        string entry = $"{DateTime.Now:HH:mm:ss} {message}";
        _deepDebug.RecordRuntimeLog(entry);
        _runLog.Add(entry);
    }

    private void RunLogTimer_OnTick(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (!_runLog.TryGetUpdatedText(out string text)) return;
        TraceLogText.Text = text;
        TraceLogText.ScrollToEnd();
    }

    private void StopRunLogTimer()
    {
        _runLogTimer.Stop();
        RunLogTimer_OnTick(null, EventArgs.Empty);
    }

    private async Task CompleteDebugAsync(string outcome)
    {
        if (_debugScope is null) return;
        await _debugScope.CompleteAsync(outcome);
        _debugScope = null;
    }
}
