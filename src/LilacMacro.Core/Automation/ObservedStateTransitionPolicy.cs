namespace LilacMacro.Core.Automation;

public enum ObservedStateTransitionOutcome
{
    DestinationReached,
    SourceRetained,
    Indeterminate,
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
}
