using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal static class MacroLoopProgressPolicy
{
    public static IReadOnlyList<PlanLoopPrototype> AdvanceCompletedLoops(
        PlanPrototype plan,
        Dictionary<PlanTaskPrototype, int> victories,
        Dictionary<PlanLoopPrototype, int> completedRuns)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(victories);
        ArgumentNullException.ThrowIfNull(completedRuns);

        List<PlanLoopPrototype> advanced = [];
        foreach (PlanLoopPrototype loop in plan.Blocks.OfType<PlanLoopPrototype>())
            Advance(loop, victories, completedRuns, advanced);
        return advanced;
    }

    public static bool IsActive(
        PlanLoopPrototype loop,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedRuns) =>
        loop.Forever || completedRuns.GetValueOrDefault(loop) < loop.RepeatCount;

    private static void Advance(
        PlanLoopPrototype loop,
        Dictionary<PlanTaskPrototype, int> victories,
        Dictionary<PlanLoopPrototype, int> completedRuns,
        ICollection<PlanLoopPrototype> advanced)
    {
        if (!IsActive(loop, completedRuns)) return;

        foreach (PlanLoopPrototype child in loop.Children.OfType<PlanLoopPrototype>())
            Advance(child, victories, completedRuns, advanced);

        if (!HasBoundedWork(loop) || !IterationComplete(loop, victories, completedRuns)) return;

        int runs = completedRuns.GetValueOrDefault(loop) + 1;
        completedRuns[loop] = runs;
        loop.CompletedRuns = runs;
        advanced.Add(loop);

        if (loop.Forever || runs < loop.RepeatCount)
            ResetIteration(loop, victories, completedRuns);
    }

    private static bool IterationComplete(
        PlanLoopPrototype loop,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedRuns)
    {
        foreach (PlanBlockPrototype child in loop.Children)
        {
            if (child is PlanTaskPrototype task && IsBounded(task) &&
                victories.GetValueOrDefault(task) < task.Target)
                return false;

            if (child is PlanLoopPrototype childLoop &&
                (childLoop.Forever || completedRuns.GetValueOrDefault(childLoop) < childLoop.RepeatCount))
                return false;
        }
        return true;
    }

    private static bool HasBoundedWork(PlanLoopPrototype loop) => loop.Children.Any(child => child switch
    {
        PlanTaskPrototype task => IsBounded(task),
        PlanLoopPrototype childLoop => !childLoop.Forever && HasBoundedWork(childLoop),
        _ => false,
    });

    private static bool IsBounded(PlanTaskPrototype task) =>
        task.Mode is not PlanTaskMode.Challenge and not PlanTaskMode.Utilities;

    private static void ResetIteration(
        PlanLoopPrototype loop,
        Dictionary<PlanTaskPrototype, int> victories,
        Dictionary<PlanLoopPrototype, int> completedRuns)
    {
        foreach (PlanBlockPrototype child in loop.Children)
        {
            if (child is PlanTaskPrototype task)
            {
                if (IsBounded(task)) victories.Remove(task);
                continue;
            }

            if (child is not PlanLoopPrototype childLoop) continue;
            completedRuns.Remove(childLoop);
            childLoop.CompletedRuns = 0;
            ResetIteration(childLoop, victories, completedRuns);
        }
    }
}
