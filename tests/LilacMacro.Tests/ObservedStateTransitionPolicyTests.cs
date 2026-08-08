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
}
