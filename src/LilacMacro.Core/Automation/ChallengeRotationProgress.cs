using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Automation;

public sealed record ChallengeTypeRotationProgress(
    RegularChallengeType Type,
    DateTimeOffset? LastCooldownEpochUtc,
    DateTimeOffset? DailyLimitUntilUtc);

public sealed record ChallengeRotationProgress(
    DateTimeOffset? AttemptEpochUtc,
    IReadOnlyList<RegularChallengeType> AttemptedTypes,
    IReadOnlyList<ChallengeTypeRotationProgress> Types)
{
    public static ChallengeRotationProgress Empty { get; } = new(null, [], []);
}
