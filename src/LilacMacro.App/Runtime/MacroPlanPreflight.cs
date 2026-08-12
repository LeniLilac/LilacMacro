using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal static class MacroPlanPreflight
{
    public static async Task ValidateAsync(
        PlanPrototype plan,
        Func<PlanTaskPrototype, CancellationToken, Task> validateTask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(validateTask);
        PlanTaskPrototype[] tasks = MacroPriorityPolicy.Flatten(plan).ToArray();
        if (tasks.Length == 0) throw new InvalidDataException("The selected plan has no tasks.");
        foreach (PlanTaskPrototype task in tasks)
        {
            if (!MacroPriorityPolicy.Supported(task))
            {
                throw new InvalidDataException(
                    $"{task.ModeLabel} runtime is not implemented; remove it before starting the plan.");
            }
            await validateTask(task, cancellationToken);
        }
    }
}
