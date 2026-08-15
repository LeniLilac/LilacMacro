using LilacMacro.App.Diagnostics;
using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal static class MacroLoopProgressReporter
{
    public static bool AdvanceAndReport(
        PlanPrototype plan,
        Dictionary<PlanTaskPrototype, int> victories,
        Dictionary<PlanLoopPrototype, int> completedRuns,
        DeepDebugSessionService deepDebug,
        Action<string> appendLog)
    {
        IReadOnlyList<PlanLoopPrototype> advanced = MacroLoopProgressPolicy.AdvanceCompletedLoops(
            plan,
            victories,
            completedRuns);
        foreach (PlanLoopPrototype loop in advanced)
        {
            appendLog($"LOOP COMPLETE | {loop.Label} | RUN {loop.CompletedRuns}");
            deepDebug.RecordEvent("macro", "loop_iteration_completed", new
            {
                Loop = loop.Label,
                loop.CompletedRuns,
                loop.Forever,
                loop.RepeatCount,
            });
        }
        return advanced.Count > 0;
    }
}
