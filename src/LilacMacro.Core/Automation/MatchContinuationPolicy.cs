namespace LilacMacro.Core.Automation;

public static class MatchContinuationPolicy
{
    public static bool ShouldRepeat(
        bool hasVerifiedTerminalOutcome,
        bool modeSupportsRepeat,
        bool sameTaskSelected) =>
        hasVerifiedTerminalOutcome && modeSupportsRepeat && sameTaskSelected;
}
