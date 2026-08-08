using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public enum ActPickerKind
{
    Story,
    Raid,
}

public sealed record ActPickerLayout(
    ActPickerKind Kind,
    PixelRect ModeBounds,
    PixelRect SelectStageBounds,
    int VerticalSpan)
{
    public const double StoryRowPitchRatio = 0.142;
    public const double DifficultyYRatio = 0.430;
    public const double NormalXRatio = 0.190;
    public const double HardXRatio = 0.326;
    public const double RaidAct1YRatio = 0.243;
    public const double RaidAct2YRatio = 0.570;
    public const double RaidAct3YRatio = 0.902;

    private static readonly StoryAct[] StoryActs = Enum.GetValues<StoryAct>();
    private static readonly StoryAct[] RaidActs = [StoryAct.Act1, StoryAct.Act2, StoryAct.Act3];

    public PixelPoint SelectStagePoint => SelectStageBounds.Center;

    public double RowPitch => VerticalSpan * StoryRowPitchRatio;

    public bool SupportsDifficulty => Kind == ActPickerKind.Story;

    public bool SupportsAct(StoryAct act) => Kind == ActPickerKind.Story || RaidActs.Contains(act);

    public PixelPoint GetActPoint(StoryAct act)
    {
        if (!SupportsAct(act))
        {
            throw new ArgumentOutOfRangeException(nameof(act), $"{act} is not available for {Kind}.");
        }

        double y = Kind switch
        {
            ActPickerKind.Story => StoryActY(act),
            ActPickerKind.Raid => ModeBounds.Center.Y + VerticalSpan * RaidActYRatio(act),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };
        return new PixelPoint(
            ModeBounds.Center.X,
            checked((int)Math.Round(y, MidpointRounding.AwayFromZero)));
    }

    public PixelPoint GetDifficultyPoint(StoryDifficulty difficulty)
    {
        if (!SupportsDifficulty)
        {
            throw new InvalidOperationException($"{Kind} has no difficulty selector.");
        }

        PixelPoint mode = ModeBounds.Center;
        double xRatio = difficulty switch
        {
            StoryDifficulty.Normal => NormalXRatio,
            StoryDifficulty.Hard => HardXRatio,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
        };
        return new PixelPoint(
            checked((int)Math.Round(mode.X + VerticalSpan * xRatio, MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(mode.Y + VerticalSpan * DifficultyYRatio, MidpointRounding.AwayFromZero)));
    }

    public static ActPickerLayout? TryCreate(
        IReadOnlyList<OcrTextRegion> regions,
        PixelSize clientSize,
        ActPickerKind kind)
    {
        ArgumentNullException.ThrowIfNull(regions);
        string modeText = kind switch
        {
            ActPickerKind.Story => "story",
            ActPickerKind.Raid => "raid",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        OcrTextRegion? mode = regions
            .Where(region => IsExact(region, modeText))
            .OrderBy(region => region.Bounds.Y)
            .ThenBy(region => region.Bounds.X)
            .ThenByDescending(region => region.RecognitionConfidence)
            .FirstOrDefault();
        OcrTextRegion? selectStage = FindSelectStage(regions);
        if (mode is null || selectStage is null) return null;

        PixelPoint modeCenter = mode.Bounds.Center;
        PixelPoint selectCenter = selectStage.Bounds.Center;
        int verticalSpan = selectCenter.Y - modeCenter.Y;
        if (verticalSpan <= 0 || selectCenter.X <= modeCenter.X) return null;

        ActPickerLayout layout = new(kind, mode.Bounds, selectStage.Bounds, verticalSpan);
        StoryAct[] acts = kind == ActPickerKind.Story ? StoryActs : RaidActs;
        bool actsAreInside = acts.Select(layout.GetActPoint).All(point => IsInside(point, clientSize));
        bool difficultiesAreInside = !layout.SupportsDifficulty || Enum.GetValues<StoryDifficulty>()
            .Select(layout.GetDifficultyPoint)
            .All(point => IsInside(point, clientSize));
        return actsAreInside && difficultiesAreInside ? layout : null;
    }

    public static OcrTextRegion? FindSelectStage(IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return regions
            .Where(region => IsExact(region, "selectstage"))
            .OrderByDescending(region => region.Bounds.Y)
            .ThenByDescending(region => region.RecognitionConfidence)
            .ThenBy(region => region.Bounds.X)
            .FirstOrDefault();
    }

    private double StoryActY(StoryAct act)
    {
        int rowsAboveMastery = act switch
        {
            StoryAct.Act1 => 6,
            StoryAct.Act2 => 5,
            StoryAct.Act3 => 4,
            StoryAct.Act4 => 3,
            StoryAct.Act5 => 2,
            StoryAct.Infinite => 1,
            StoryAct.Mastery => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(act)),
        };
        return SelectStagePoint.Y - rowsAboveMastery * RowPitch;
    }

    private static double RaidActYRatio(StoryAct act) => act switch
    {
        StoryAct.Act1 => RaidAct1YRatio,
        StoryAct.Act2 => RaidAct2YRatio,
        StoryAct.Act3 => RaidAct3YRatio,
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private static bool IsExact(OcrTextRegion region, string target) =>
        OcrRuleEngine.Normalize(region.Text).Equals(target, StringComparison.Ordinal);

    private static bool IsInside(PixelPoint point, PixelSize size) =>
        point.X >= 0 && point.Y >= 0 && point.X < size.Width && point.Y < size.Height;
}
