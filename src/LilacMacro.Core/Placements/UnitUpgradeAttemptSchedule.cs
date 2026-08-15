namespace LilacMacro.Core.Placements;

public sealed record UnitUpgradeAttempt(int Number, int DelayBeforeMilliseconds);

public static class UnitUpgradeAttemptSchedule
{
    public static IReadOnlyList<UnitUpgradeAttempt> Create(
        int count,
        int betweenAttemptsMilliseconds)
    {
        if (count is < 1 or > PlacementSetupRules.MaximumUpgradeCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        PlacementSetupRules.ValidateActionDelay(betweenAttemptsMilliseconds);
        return Enumerable.Range(1, count)
            .Select(number => new UnitUpgradeAttempt(
                number,
                number == 1 ? 0 : betweenAttemptsMilliseconds))
            .ToArray();
    }
}
