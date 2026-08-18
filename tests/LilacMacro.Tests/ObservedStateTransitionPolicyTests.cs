using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class ObservedStateTransitionPolicyTests
{
    [Theory]
    [InlineData(false, true, ObservedStateTransitionOutcome.DestinationReached)]
    [InlineData(true, true, ObservedStateTransitionOutcome.DestinationReached)]
    [InlineData(true, false, ObservedStateTransitionOutcome.SourceRetained)]
    [InlineData(false, false, ObservedStateTransitionOutcome.Indeterminate)]
    public void Classify_PrefersDestinationAndDistinguishesRetainedSource(
        bool sourceObserved,
        bool destinationObserved,
        ObservedStateTransitionOutcome expected)
    {
        Assert.Equal(
            expected,
            ObservedStateTransitionPolicy.Classify(sourceObserved, destinationObserved));
    }

    [Theory]
    [InlineData(ObservedStateTransitionOutcome.DestinationReached, 4, 8, ObservedStateTransitionDecision.Complete)]
    [InlineData(ObservedStateTransitionOutcome.SourceRetained, 0, 0, ObservedStateTransitionDecision.RetrySourceAction)]
    [InlineData(ObservedStateTransitionOutcome.SourceRetained, 4, 0, ObservedStateTransitionDecision.Exhausted)]
    [InlineData(ObservedStateTransitionOutcome.Indeterminate, 0, 0, ObservedStateTransitionDecision.ObserveAgain)]
    [InlineData(ObservedStateTransitionOutcome.Indeterminate, 0, 8, ObservedStateTransitionDecision.Exhausted)]
    public void Decide_SeparatesRetryObservationAndExhaustion(
        ObservedStateTransitionOutcome outcome,
        int actions,
        int indeterminate,
        ObservedStateTransitionDecision expected)
    {
        Assert.Equal(
            expected,
            ObservedStateTransitionPolicy.Decide(
                outcome,
                actions,
                indeterminate,
                new ObservedStateTransitionBudget()));
    }

    [Fact]
    public void TimedBudgetKeepsObservingAfterActionAndObservationCaps()
    {
        ObservedStateTransitionBudget budget = new()
        {
            MaximumActionAttempts = 1,
            MaximumIndeterminateObservations = 1,
            RetryWindow = TimeSpan.FromMinutes(2),
            RetryIntervalMilliseconds = 250,
        };

        Assert.Equal(
            ObservedStateTransitionDecision.ObserveAgain,
            ObservedStateTransitionPolicy.Decide(
                ObservedStateTransitionOutcome.SourceRetained,
                completedActionAttempts: 1,
                completedIndeterminateObservations: 0,
                budget));
        Assert.Equal(
            ObservedStateTransitionDecision.ObserveAgain,
            ObservedStateTransitionPolicy.Decide(
                ObservedStateTransitionOutcome.Indeterminate,
                completedActionAttempts: 1,
                completedIndeterminateObservations: 1,
                budget));
        Assert.Equal(
            ObservedStateTransitionDecision.Exhausted,
            ObservedStateTransitionPolicy.Decide(
                ObservedStateTransitionOutcome.SourceRetained,
                completedActionAttempts: 1,
                completedIndeterminateObservations: 1,
                budget,
                retryWindowExpired: true));
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(1, 600)]
    [InlineData(2, 1200)]
    [InlineData(3, 1600)]
    [InlineData(30, 1600)]
    public void ObservationDelay_ExpandsAndCaps(int observations, int expectedMilliseconds)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            ObservedStateTransitionPolicy.ObservationDelay(
                observations,
                new ObservedStateTransitionBudget()));
    }

    [Fact]
    public void Decide_RejectsInvalidBudget()
    {
        ObservedStateTransitionBudget budget = new() { MaximumActionAttempts = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObservedStateTransitionPolicy.Decide(
                ObservedStateTransitionOutcome.SourceRetained,
                0,
                0,
                budget));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObservedStateTransitionPolicy.Decide(
                ObservedStateTransitionOutcome.SourceRetained,
                0,
                0,
                new ObservedStateTransitionBudget
                {
                    RetryWindow = TimeSpan.FromSeconds(-1),
                }));
    }

    [Fact]
    public void Decide_RejectsNegativeCounters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObservedStateTransitionPolicy.Decide(
                ObservedStateTransitionOutcome.SourceRetained,
                -1,
                0,
                new ObservedStateTransitionBudget()));
    }
}
