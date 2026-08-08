using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Automation;

public sealed class ChallengeRotationPolicy
{
    private readonly HashSet<RegularChallengeType> _attempted;
    private readonly Dictionary<RegularChallengeType, ChallengeTypeRotationProgress> _types;
    private DateTimeOffset? _attemptEpochUtc;

    public ChallengeRotationPolicy(ChallengeRotationProgress? progress = null)
    {
        ChallengeRotationProgress source = progress ?? ChallengeRotationProgress.Empty;
        _attemptEpochUtc = source.AttemptEpochUtc;
        _attempted = [.. source.AttemptedTypes];
        _types = source.Types.ToDictionary(item => item.Type);
    }

    public bool CanAttempt(RegularChallengeType type, DateTimeOffset now)
    {
        Advance(now);
        return !_attempted.Contains(type) && !IsDailyLimited(type, now);
    }

    public void ObserveAvailable(RegularChallengeType type, DateTimeOffset now)
    {
        Advance(now);
        _attempted.Add(type);
        _types[type] = new ChallengeTypeRotationProgress(type, null, null);
    }

    public bool ObserveCooldown(RegularChallengeType type, DateTimeOffset now)
    {
        Advance(now);
        DateTimeOffset epoch = ResetEpoch(now);
        _attempted.Add(type);
        _types.TryGetValue(type, out ChallengeTypeRotationProgress? prior);
        bool crossedCompleteReset = prior?.LastCooldownEpochUtc is DateTimeOffset priorEpoch && priorEpoch < epoch;
        DateTimeOffset? limitedUntil = crossedCompleteReset ? NextUtcMidnight(now) : prior?.DailyLimitUntilUtc;
        _types[type] = new ChallengeTypeRotationProgress(type, epoch, limitedUntil);
        return crossedCompleteReset;
    }

    public bool IsDailyLimited(RegularChallengeType type, DateTimeOffset now)
    {
        Advance(now);
        return _types.TryGetValue(type, out ChallengeTypeRotationProgress? progress) &&
            progress.DailyLimitUntilUtc is DateTimeOffset until && now < until;
    }

    public DateTimeOffset NextEligibleUtc(
        IReadOnlyCollection<RegularChallengeType> enabledTypes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(enabledTypes);
        if (enabledTypes.Count == 0) throw new ArgumentException("At least one challenge type is required.", nameof(enabledTypes));
        Advance(now);
        if (enabledTypes.Any(type => CanAttempt(type, now))) return now;
        return enabledTypes.All(type => IsDailyLimited(type, now))
            ? NextUtcMidnight(now)
            : NextGlobalReset(now);
    }

    public ChallengeRotationProgress Snapshot(DateTimeOffset now)
    {
        Advance(now);
        return new ChallengeRotationProgress(
            _attemptEpochUtc,
            _attempted.OrderBy(type => type).ToArray(),
            _types.Values.OrderBy(item => item.Type).ToArray());
    }

    public static DateTimeOffset ResetEpoch(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        int minute = utc.Minute < 30 ? 0 : 30;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, TimeSpan.Zero);
    }

    public static DateTimeOffset NextGlobalReset(DateTimeOffset value) => ResetEpoch(value).AddMinutes(30);

    public static DateTimeOffset NextUtcMidnight(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
    }

    private void Advance(DateTimeOffset now)
    {
        DateTimeOffset epoch = ResetEpoch(now);
        if (_attemptEpochUtc != epoch)
        {
            _attemptEpochUtc = epoch;
            _attempted.Clear();
        }

        foreach ((RegularChallengeType type, ChallengeTypeRotationProgress progress) in _types.ToArray())
        {
            if (progress.DailyLimitUntilUtc is not DateTimeOffset until || now < until) continue;
            _types[type] = progress with { DailyLimitUntilUtc = null, LastCooldownEpochUtc = null };
        }
    }
}
