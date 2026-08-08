using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public sealed record ExpeditionMapPickerLayout(
    PixelRect DifficultyBounds,
    PixelRect SelectStageBounds,
    int VerticalSpan)
{
    public const double MinusXRatio = -0.087;
    public const double PlusXRatio = 0.564;
    public const double ControlYRatio = 0.173;

    public PixelPoint MinusPoint => GetControlPoint(MinusXRatio);

    public PixelPoint PlusPoint => GetControlPoint(PlusXRatio);

    public PixelPoint SelectStagePoint => SelectStageBounds.Center;

    public static int GetIncreaseClickCount(int difficulty) => difficulty switch
    {
        >= 1 and <= 3 => difficulty - 1,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    public static OcrTargetMatch? FindSelectedMap(
        OcrTargetRule target,
        IReadOnlyList<OcrTextRegion> regions,
        PixelRect listBounds)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(regions);
        return OcrRuleEngine.FindAllTargets(target, regions)
            .Where(match => match.Region.Bounds.X > listBounds.X)
            .Where(match => match.Region.Bounds.Y < listBounds.Y)
            .OrderBy(match => match.Region.Bounds.Y)
            .ThenBy(match => match.Region.Bounds.X)
            .ThenByDescending(match => match.Region.RecognitionConfidence)
            .FirstOrDefault();
    }

    public static ExpeditionMapPickerLayout? TryCreate(
        IReadOnlyList<OcrTextRegion> regions,
        PixelSize clientSize)
    {
        ArgumentNullException.ThrowIfNull(regions);
        OcrTextRegion? difficulty = FindTopmostDifficulty(regions);
        OcrTextRegion? selectStage = FindSelectStage(regions);
        if (difficulty is null || selectStage is null) return null;

        int verticalSpan = selectStage.Bounds.Center.Y - difficulty.Bounds.Center.Y;
        if (verticalSpan <= 0) return null;

        ExpeditionMapPickerLayout layout = new(
            difficulty.Bounds,
            selectStage.Bounds,
            verticalSpan);
        return IsInside(layout.MinusPoint, clientSize) &&
            IsInside(layout.PlusPoint, clientSize) &&
            IsInside(layout.SelectStagePoint, clientSize)
            ? layout
            : null;
    }

    private PixelPoint GetControlPoint(double xRatio)
    {
        PixelPoint anchor = DifficultyBounds.Center;
        return new PixelPoint(
            checked((int)Math.Round(
                anchor.X + VerticalSpan * xRatio,
                MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(
                anchor.Y + VerticalSpan * ControlYRatio,
                MidpointRounding.AwayFromZero)));
    }

    private static OcrTextRegion? FindTopmostDifficulty(
        IReadOnlyList<OcrTextRegion> regions) => regions
        .Where(region => OcrRuleEngine.Normalize(region.Text)
            .Contains("difficulty", StringComparison.Ordinal))
        .OrderBy(region => region.Bounds.Y)
        .ThenBy(region => OcrRuleEngine.Normalize(region.Text) == "difficulty" ? 0 : 1)
        .ThenBy(region => region.Bounds.X)
        .ThenByDescending(region => region.RecognitionConfidence)
        .FirstOrDefault();

    private static OcrTextRegion? FindSelectStage(
        IReadOnlyList<OcrTextRegion> regions) => regions
        .Where(region => OcrRuleEngine.Normalize(region.Text)
            .Contains("selectstage", StringComparison.Ordinal))
        .OrderByDescending(region => region.Bounds.Y)
        .ThenBy(region => OcrRuleEngine.Normalize(region.Text).Length)
        .ThenByDescending(region => region.RecognitionConfidence)
        .ThenBy(region => region.Bounds.X)
        .FirstOrDefault();

    private static bool IsInside(PixelPoint point, PixelSize size) =>
        point.X >= 0 && point.Y >= 0 && point.X < size.Width && point.Y < size.Height;
}
