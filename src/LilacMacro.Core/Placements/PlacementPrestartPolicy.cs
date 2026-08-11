namespace LilacMacro.Core.Placements;

public enum MatchStartBoundaryDecision
{
    ClickStart,
    AutoStarted,
    Indeterminate,
}

public static class PlacementPrestartPolicy
{
    public const int MaximumGuaranteedDelayMilliseconds = 30_000;
    public const int RequiredStartScreenMisses = 3;

    public static long CalculateGuaranteedDelayMilliseconds(PlacementRouteSetup route)
    {
        ArgumentNullException.ThrowIfNull(route);
        long total = 0;
        foreach (PlacementStep step in route.Steps)
        {
            if (step.Kind == PlacementStepKind.StartGame) break;
            total += step.DelayAfterMilliseconds;
            if (step.Kind == PlacementStepKind.Delay)
            {
                total += step.DelayDurationMilliseconds;
            }
        }
        return total;
    }

    public static void ValidateGuaranteedDelay(PlacementRouteSetup route)
    {
        long total = CalculateGuaranteedDelayMilliseconds(route);
        if (total > MaximumGuaranteedDelayMilliseconds)
        {
            throw new InvalidDataException(
                $"Guaranteed prestart delays total {total} ms; the maximum is " +
                $"{MaximumGuaranteedDelayMilliseconds} ms.");
        }
    }

    public static MatchStartBoundaryDecision DecideBoundary(
        int consecutiveStartScreenMisses,
        bool hasLiveRuntimeEvidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveStartScreenMisses);
        if (consecutiveStartScreenMisses == 0) return MatchStartBoundaryDecision.ClickStart;
        return consecutiveStartScreenMisses >= RequiredStartScreenMisses && hasLiveRuntimeEvidence
            ? MatchStartBoundaryDecision.AutoStarted
            : MatchStartBoundaryDecision.Indeterminate;
    }
}
