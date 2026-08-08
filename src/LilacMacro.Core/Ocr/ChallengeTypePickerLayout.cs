using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public enum RegularChallengeType
{
    Trait,
    Stat,
    Sprite,
}

public sealed record ChallengeTypePickerLayout(
    PixelRect ChallengeBounds,
    PixelRect DailyBounds,
    PixelRect WeeklyBounds,
    int Scale)
{
    public const double TargetXRatio = 2.850;
    public const double TraitYRatio = 0.485;
    public const double StatYRatio = 1.010;
    public const double SpriteYRatio = 1.535;

    public PixelPoint GetTypePoint(RegularChallengeType type)
    {
        double yRatio = type switch
        {
            RegularChallengeType.Trait => TraitYRatio,
            RegularChallengeType.Stat => StatYRatio,
            RegularChallengeType.Sprite => SpriteYRatio,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        PixelPoint challenge = ChallengeBounds.Center;
        return new PixelPoint(
            checked((int)Math.Round(challenge.X + Scale * TargetXRatio, MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(challenge.Y + Scale * yRatio, MidpointRounding.AwayFromZero)));
    }

    public static ChallengeTypePickerLayout? TryCreate(
        IReadOnlyList<OcrTextRegion> regions,
        PixelSize clientSize)
    {
        ArgumentNullException.ThrowIfNull(regions);
        OcrTextRegion? challenge = FindExact(regions, "challenge", "challenges");
        OcrTextRegion? daily = FindExact(regions, "dailychallenge");
        OcrTextRegion? weekly = FindExact(regions, "weeklychallenge");
        if (challenge is null || daily is null || weekly is null) return null;

        PixelPoint challengeCenter = challenge.Bounds.Center;
        PixelPoint dailyCenter = daily.Bounds.Center;
        PixelPoint weeklyCenter = weekly.Bounds.Center;
        if (dailyCenter.Y <= challengeCenter.Y || weeklyCenter.Y <= dailyCenter.Y) return null;

        int scale = weeklyCenter.Y - challengeCenter.Y;
        ChallengeTypePickerLayout layout = new(
            challenge.Bounds,
            daily.Bounds,
            weekly.Bounds,
            scale);
        bool pointsAreInside = Enum.GetValues<RegularChallengeType>()
            .Select(layout.GetTypePoint)
            .All(point => IsInside(point, clientSize));
        return pointsAreInside ? layout : null;
    }

    private static OcrTextRegion? FindExact(
        IReadOnlyList<OcrTextRegion> regions,
        params string[] normalizedTargets) => regions
        .Where(region => normalizedTargets.Contains(OcrRuleEngine.Normalize(region.Text)))
        .OrderBy(region => region.Bounds.Y)
        .ThenBy(region => region.Bounds.X)
        .ThenByDescending(region => region.RecognitionConfidence)
        .FirstOrDefault();

    private static bool IsInside(PixelPoint point, PixelSize size) =>
        point.X >= 0 && point.Y >= 0 && point.X < size.Width && point.Y < size.Height;
}
