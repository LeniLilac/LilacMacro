using LilacMacro.Core.Automation;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class ChallengeRotationPolicyTests
{
    [Fact]
    public void CooldownAcrossACompleteResetMarksOnlyThatTypeDailyLimited()
    {
        DateTimeOffset first = Utc(10, 10);
        ChallengeRotationPolicy policy = new();
        Assert.False(policy.ObserveCooldown(RegularChallengeType.Trait, first));

        Assert.True(policy.ObserveCooldown(RegularChallengeType.Trait, Utc(10, 31)));
        Assert.True(policy.IsDailyLimited(RegularChallengeType.Trait, Utc(10, 31)));
        Assert.True(policy.CanAttempt(RegularChallengeType.Stat, Utc(10, 31)));
    }

    [Fact]
    public void AvailabilityBetweenEpochsClearsCooldownEvidence()
    {
        ChallengeRotationPolicy policy = new();
        policy.ObserveCooldown(RegularChallengeType.Sprite, Utc(10, 10));
        policy.ObserveAvailable(RegularChallengeType.Sprite, Utc(10, 31));

        Assert.False(policy.ObserveCooldown(RegularChallengeType.Sprite, Utc(11, 1)));
        Assert.False(policy.IsDailyLimited(RegularChallengeType.Sprite, Utc(11, 1)));
    }

    [Fact]
    public void DailyLimitExpiresAtUtcMidnight()
    {
        ChallengeRotationPolicy policy = new();
        policy.ObserveCooldown(RegularChallengeType.Trait, Utc(23, 10));
        policy.ObserveCooldown(RegularChallengeType.Trait, Utc(23, 31));

        Assert.False(policy.CanAttempt(RegularChallengeType.Trait, Utc(23, 45)));
        Assert.True(policy.CanAttempt(RegularChallengeType.Trait, new DateTimeOffset(2026, 8, 8, 0, 1, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void AllDailyLimitedTypesWaitUntilMidnight()
    {
        ChallengeRotationPolicy policy = new();
        RegularChallengeType[] enabled = [RegularChallengeType.Trait, RegularChallengeType.Stat];
        foreach (RegularChallengeType type in enabled)
        {
            policy.ObserveCooldown(type, Utc(10, 1));
            policy.ObserveCooldown(type, Utc(10, 31));
        }

        Assert.Equal(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero), policy.NextEligibleUtc(enabled, Utc(10, 31)));
    }

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 8, 7, hour, minute, 0, TimeSpan.Zero);
}
