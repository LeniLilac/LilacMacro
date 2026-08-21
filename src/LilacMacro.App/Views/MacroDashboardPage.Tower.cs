using LilacMacro.App.Debugging;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    private async Task ResetLobbyAtPlanStartAsync(
        string device,
        HashSet<string> redeemedCodes,
        Func<bool> startupSettingsNormalized,
        Action markStartupSettingsNormalized,
        CancellationToken cancellationToken)
    {
        bool normalizeStartupSettings = !startupSettingsNormalized();
        await _lobbyReset.ResetAsync(
            device,
            normalizeStartupSettings,
            redeemedCodes,
            cancellationToken);
        if (normalizeStartupSettings) markStartupSettingsNormalized();
    }

    private async Task<PlanTaskPrototype> CompleteAlreadyClearedTowerGoalAsync(
        PlanPrototype plan,
        PlanTaskPrototype task,
        StoryWireTestOptions options,
        StoryWireTestResult result,
        string device,
        HashSet<string> redeemedCodes,
        Action madeProgress,
        CancellationToken cancellationToken)
    {
        MatchTaskProgressPolicy.ApplyObservedTowerAvailability(
            task, options.TowerFloor, _victories, _defeats);
        _recovery.MarkTaskSucceeded(task);
        madeProgress();
        if (MacroLoopProgressReporter.AdvanceAndReport(
                plan, _victories, _completedLoopRuns, _deepDebug, AppendLog))
            RefreshUpcomingTasks(plan);
        QueueRuntimeProgressSave();
        AppendLog(result.Status);
        _currentTask = null;
        RefreshUpcomingTasks(plan);
        await _lobbyReset.ResetAsync(
            device,
            normalizeStartupSettings: false,
            redeemedCodes,
            cancellationToken);
        return task;
    }
}
