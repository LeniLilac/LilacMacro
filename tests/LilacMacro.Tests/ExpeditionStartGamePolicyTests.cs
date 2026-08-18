using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class ExpeditionStartGamePolicyTests
{
    [Fact]
    public void RetryWindowIsBoundedAndClampsItsFinalDelay()
    {
        Assert.True(ExpeditionStartGamePolicy.IsWithinRetryWindow(TimeSpan.Zero));
        Assert.True(ExpeditionStartGamePolicy.IsWithinRetryWindow(
            ExpeditionStartGamePolicy.RetryWindow - TimeSpan.FromMilliseconds(1)));
        Assert.False(ExpeditionStartGamePolicy.IsWithinRetryWindow(
            ExpeditionStartGamePolicy.RetryWindow));
        Assert.Equal(
            TimeSpan.FromMilliseconds(250),
            ExpeditionStartGamePolicy.RetryDelay(TimeSpan.Zero));
        Assert.Equal(
            TimeSpan.FromMilliseconds(1),
            ExpeditionStartGamePolicy.RetryDelay(
                ExpeditionStartGamePolicy.RetryWindow - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(
            TimeSpan.Zero,
            ExpeditionStartGamePolicy.RetryDelay(ExpeditionStartGamePolicy.RetryWindow));
    }

    [Fact]
    public void RetryWindowRejectsNegativeElapsedTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpeditionStartGamePolicy.IsWithinRetryWindow(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpeditionStartGamePolicy.RetryDelay(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void MatchLoadPolicyProvidesTheSharedTransitionDeadline()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), MatchLoadPolicy.RetryWindow);
        Assert.Equal(250, MatchLoadPolicy.RetryMilliseconds);
        Assert.Equal(
            MatchLoadPolicy.RetryWindow,
            MatchLoadPolicy.TransitionBudget.RetryWindow);
        Assert.Equal(
            MatchLoadPolicy.RetryMilliseconds,
            MatchLoadPolicy.TransitionBudget.RetryIntervalMilliseconds);
    }
}
