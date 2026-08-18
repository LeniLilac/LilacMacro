using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

internal static class UpcomingTaskRowFactory
{
    public static IReadOnlyList<UpcomingTaskRow> Build(
        PlanPrototype plan,
        PlanTaskPrototype? currentTask,
        IReadOnlyDictionary<PlanTaskPrototype, int> victories,
        IReadOnlyDictionary<PlanLoopPrototype, int> completedLoopRuns,
        DateTimeOffset now,
        Func<PlanTaskPrototype, DateTimeOffset, DateTimeOffset> eligibleAt,
        Func<PlanTaskPrototype, bool>? isIndefinitelyQuarantined = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(victories);
        ArgumentNullException.ThrowIfNull(eligibleAt);

        return MacroPriorityPolicy.Flatten(plan, completedLoopRuns)
            .Where(task => MacroPriorityPolicy.IsPending(task, victories))
            .Select((task, index) =>
            {
                bool quarantined = isIndefinitelyQuarantined?.Invoke(task) == true;
                string detail = quarantined
                    ? "UTILITY \u00B7 NO PRIORITY"
                    : ReferenceEquals(task, currentTask)
                        ? $"CURRENT \u00B7 PRIORITY {task.Priority}"
                        : $"{task.ModeLabel.ToUpperInvariant()} \u00B7 PRIORITY {task.Priority}";
                string progress = quarantined
                    ? "OPPORTUNISTIC ONLY"
                    : eligibleAt(task, now) is DateTimeOffset until && until > now
                        ? $"NEXT {until:MM-dd HH:mm}Z"
                        : task.Mode is PlanTaskMode.Utilities or PlanTaskMode.Challenge
                            ? task.TargetLabel
                            : $"{victories.GetValueOrDefault(task)}/{task.Target} W";
                return new UpcomingTaskRow(index + 1, task.Name, detail, progress);
            })
            .ToArray();
    }
}

internal sealed record UpcomingTaskRow(int Position, string Name, string Detail, string Progress);
