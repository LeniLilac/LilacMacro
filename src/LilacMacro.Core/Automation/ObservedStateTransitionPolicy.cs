namespace LilacMacro.Core.Automation;

public enum ObservedStateTransitionOutcome
{
    DestinationReached,
    SourceRetained,
    Indeterminate,
}

public enum ObservedStateTransitionDecision
{
    Complete,
    RetrySourceAction,
    ObserveAgain,
    Exhausted,
}

public sealed record ObservedStateTransitionBudget
{
    public const int DefaultMaximumActionAttempts = 4;
    public const int DefaultMaximumIndeterminateObservations = 8;
    public const int DefaultInitialObservationDelayMilliseconds = 300;
    public const int DefaultMaximumObservationDelayMilliseconds = 1600;

    public int MaximumActionAttempts { get; init; } = DefaultMaximumActionAttempts;
    public int MaximumIndeterminateObservations { get; init; } = DefaultMaximumIndeterminateObservations;
    public int InitialObservationDelayMilliseconds { get; init; } = DefaultInitialObservationDelayMilliseconds;
    public int MaximumObservationDelayMilliseconds { get; init; } = DefaultMaximumObservationDelayMilliseconds;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumActionAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumIndeterminateObservations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(InitialObservationDelayMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumObservationDelayMilliseconds,
            InitialObservationDelayMilliseconds);
    }
}

public static class ObservedStateTransitionPolicy
{
    public static ObservedStateTransitionOutcome Classify(
        bool sourceObserved,
        bool destinationObserved)
    {
        if (destinationObserved)
            return ObservedStateTransitionOutcome.DestinationReached;
        return sourceObserved
            ? ObservedStateTransitionOutcome.SourceRetained
            : ObservedStateTransitionOutcome.Indeterminate;
    }

    public static ObservedStateTransitionDecision Decide(
        ObservedStateTransitionOutcome outcome,
        int completedActionAttempts,
        int completedIndeterminateObservations,
        ObservedStateTransitionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(completedActionAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(completedIndeterminateObservations);

        return outcome switch
        {
            ObservedStateTransitionOutcome.DestinationReached => ObservedStateTransitionDecision.Complete,
            ObservedStateTransitionOutcome.SourceRetained =>
                completedActionAttempts < budget.MaximumActionAttempts
                    ? ObservedStateTransitionDecision.RetrySourceAction
                    : ObservedStateTransitionDecision.Exhausted,
            ObservedStateTransitionOutcome.Indeterminate =>
                completedIndeterminateObservations < budget.MaximumIndeterminateObservations
                    ? ObservedStateTransitionDecision.ObserveAgain
                    : ObservedStateTransitionDecision.Exhausted,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
    }

    public static TimeSpan ObservationDelay(
        int completedIndeterminateObservations,
        ObservedStateTransitionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(completedIndeterminateObservations);

        int exponent = Math.Min(completedIndeterminateObservations, 30);
        long expanded = (long)budget.InitialObservationDelayMilliseconds << exponent;
        return TimeSpan.FromMilliseconds(Math.Min(expanded, budget.MaximumObservationDelayMilliseconds));
    }
}
