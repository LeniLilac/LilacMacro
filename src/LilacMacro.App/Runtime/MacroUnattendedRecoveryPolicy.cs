using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal static class MacroUnattendedRecoveryPolicy
{
    public const int TaskFailuresBeforeQuarantine = 3;
    public const int MaximumOpportunisticUtilityAttempts = 1;
    public static readonly TimeSpan TaskQuarantineDuration = TimeSpan.FromMinutes(5);

    public static TimeSpan RetryDelay(int consecutiveFailures) => consecutiveFailures switch
    {
        <= 0 => throw new ArgumentOutOfRangeException(nameof(consecutiveFailures)),
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        3 => TimeSpan.FromSeconds(15),
        _ => TimeSpan.FromSeconds(30),
    };

    public static bool ShouldQuarantineTask(int consecutiveTaskFailures) =>
        consecutiveTaskFailures >= TaskFailuresBeforeQuarantine;

    public static bool ShouldQuarantineIndefinitely(
        PlanTaskMode mode,
        int consecutiveTaskFailures) =>
        mode == PlanTaskMode.Utilities && ShouldQuarantineTask(consecutiveTaskFailures);

    public static bool CanAttemptOpportunistically(
        PlanTaskMode mode,
        bool taskSwitchAvailable,
        int attemptsAtBoundary) =>
        mode == PlanTaskMode.Utilities &&
        taskSwitchAvailable &&
        attemptsAtBoundary < MaximumOpportunisticUtilityAttempts;
}
