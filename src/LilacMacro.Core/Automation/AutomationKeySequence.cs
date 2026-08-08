using System.Collections.ObjectModel;

namespace LilacMacro.Core.Automation;

public sealed class AutomationKeySequence
{
    public const int MaximumSteps = 32;
    public const int MaximumTotalHoldMilliseconds = 600000;
    private readonly ReadOnlyCollection<AutomationKeyPress> _steps;

    private AutomationKeySequence(AutomationKeyPress[] steps)
    {
        _steps = Array.AsReadOnly(steps);
    }

    public IReadOnlyList<AutomationKeyPress> Steps => _steps;

    public int TotalHoldMilliseconds => _steps.Sum(step => step.HoldMilliseconds);

    public static AutomationKeySequence Create(IEnumerable<AutomationKeyPress> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        AutomationKeyPress[] copy = steps.ToArray();
        if (copy.Length is < 1 or > MaximumSteps)
        {
            throw new InvalidDataException($"Key chain must contain 1 to {MaximumSteps} keys.");
        }
        if (copy.Any(step => !KeyboardKey.IsSupportedAutomationKey(step.VirtualKey) ||
                             step.HoldMilliseconds is < AutomationKeyPress.MinimumHoldMilliseconds or
                                 > AutomationKeyPress.MaximumHoldMilliseconds))
        {
            throw new InvalidDataException("The key chain contains an invalid key or hold time.");
        }
        int total = copy.Sum(step => step.HoldMilliseconds);
        if (total > MaximumTotalHoldMilliseconds)
        {
            throw new InvalidDataException(
                $"Total hold time must not exceed {MaximumTotalHoldMilliseconds} ms.");
        }
        return new AutomationKeySequence(copy);
    }
}
