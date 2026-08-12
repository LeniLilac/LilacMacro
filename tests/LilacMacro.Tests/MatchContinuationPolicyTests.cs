using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class MatchContinuationPolicyTests
{
    [Fact]
    public void RepeatsVerifiedSupportedSameTask() =>
        Assert.True(MatchContinuationPolicy.ShouldRepeat(
            hasVerifiedTerminalOutcome: true,
            modeSupportsRepeat: true,
            sameTaskSelected: true));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ResetsWhenAnyRequiredConditionIsAbsent(
        bool hasVerifiedTerminalOutcome,
        bool modeSupportsRepeat,
        bool sameTaskSelected) =>
        Assert.False(MatchContinuationPolicy.ShouldRepeat(
            hasVerifiedTerminalOutcome,
            modeSupportsRepeat,
            sameTaskSelected));
}
