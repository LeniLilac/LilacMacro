using LilacMacro.Runtime.Normalization;

namespace LilacMacro.Tests;

public sealed class SettingsOpenAttemptPolicyTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, true)]
    [InlineData(5, 2, false)]
    [InlineData(6, 2, true)]
    [InlineData(11, 3, false)]
    [InlineData(12, 3, true)]
    [InlineData(17, 4, false)]
    public void ShouldAttempt_DistributesFourAttemptsAcrossObservationWindow(
        int observation,
        int completedAttempts,
        bool expected)
    {
        Assert.Equal(expected, SettingsOpenAttemptPolicy.ShouldAttempt(observation, completedAttempts));
    }
}
