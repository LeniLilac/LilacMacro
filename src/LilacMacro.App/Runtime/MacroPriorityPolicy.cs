using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal static class MacroPriorityPolicy
{
    public static IReadOnlyList<PlanTaskPrototype> Flatten(PlanPrototype plan)
        => Flatten(plan, new Dictionary<PlanLoopPrototype, int>());

    public static IReadOnlyList<PlanTaskPrototype> Flatten(
        PlanPrototype plan,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedRuns)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(completedRuns);
        List<PlanTaskPrototype> tasks = [];
        foreach (PlanBlockPrototype block in plan.Blocks) Add(block, completedRuns, tasks);
        return tasks.OrderBy(task => task.Priority).ToArray();
    }

    public static PlanTaskPrototype? Select(
        PlanPrototype plan,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories,
        Func<PlanTaskPrototype, bool>? isEligible = null) => Flatten(plan)
        .FirstOrDefault(task => IsPending(task, victories) && (isEligible?.Invoke(task) ?? true));

    public static PlanTaskPrototype? Select(
        PlanPrototype plan,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedRuns,
        Func<PlanTaskPrototype, bool>? isEligible = null) => Flatten(plan, completedRuns)
        .FirstOrDefault(task => IsPending(task, victories) && (isEligible?.Invoke(task) ?? true));

    public static PlanTaskPrototype? SelectEligibleAt(
        PlanPrototype plan,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories,
        DateTimeOffset observedAt,
        Func<PlanTaskPrototype, DateTimeOffset, DateTimeOffset> eligibleAt,
        Func<PlanTaskPrototype, DateTimeOffset, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(eligibleAt);
        ArgumentNullException.ThrowIfNull(isEnabled);
        return Select(
            plan,
            victories,
            task => observedAt >= eligibleAt(task, observedAt) &&
                    isEnabled(task, observedAt));
    }

    public static PlanTaskPrototype? SelectEligibleAt(
        PlanPrototype plan,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedRuns,
        DateTimeOffset observedAt,
        Func<PlanTaskPrototype, DateTimeOffset, DateTimeOffset> eligibleAt,
        Func<PlanTaskPrototype, DateTimeOffset, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(eligibleAt);
        ArgumentNullException.ThrowIfNull(isEnabled);
        return Select(
            plan,
            victories,
            completedRuns,
            task => observedAt >= eligibleAt(task, observedAt) &&
                    isEnabled(task, observedAt));
    }

    public static bool IsPending(
        PlanTaskPrototype task,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories) =>
        task.Mode is PlanTaskMode.Challenge or PlanTaskMode.Utilities ||
        victories.GetValueOrDefault(task) < task.Target;

    public static bool Supported(PlanTaskPrototype task) =>
        task.Mode is PlanTaskMode.Story or PlanTaskMode.Raid or PlanTaskMode.Challenge or PlanTaskMode.Expedition or PlanTaskMode.Event or PlanTaskMode.Utilities;

    private static void Add(
        PlanBlockPrototype block,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedRuns,
        ICollection<PlanTaskPrototype> tasks)
    {
        if (block is PlanTaskPrototype task) tasks.Add(task);
        else if (block is PlanLoopPrototype loop && MacroLoopProgressPolicy.IsActive(loop, completedRuns))
            foreach (PlanBlockPrototype child in loop.Children) Add(child, completedRuns, tasks);
    }
}
