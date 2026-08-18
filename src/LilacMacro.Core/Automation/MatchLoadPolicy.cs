namespace LilacMacro.Core.Automation;

public static class MatchLoadPolicy
{
    public static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(2);
    public const int RetryMilliseconds = 250;

    public static ObservedStateTransitionBudget TransitionBudget => new()
    {
        RetryWindow = RetryWindow,
        RetryIntervalMilliseconds = RetryMilliseconds,
    };

    public static bool IsWithinRetryWindow(TimeSpan elapsed)
    {
        EnsureNonNegative(elapsed);
        return elapsed < RetryWindow;
    }

    public static TimeSpan RetryDelay(TimeSpan elapsed)
    {
        EnsureNonNegative(elapsed);
        TimeSpan remaining = RetryWindow - elapsed;
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;
        TimeSpan interval = TimeSpan.FromMilliseconds(RetryMilliseconds);
        return remaining < interval ? remaining : interval;
    }

    private static void EnsureNonNegative(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
    }
}
