using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Vision;

public enum VisualAnchorMatchStatus
{
    Matched,
    BelowThreshold,
    Ambiguous,
    RequiresOcr,
}

public sealed record VisualAnchorMatcherOptions
{
    public int HorizontalSearchRadius { get; init; } = 16;

    public int VerticalSearchRadius { get; init; } = 16;

    public int SearchStep { get; init; } = 2;

    public IReadOnlyList<double> ScaleFactors { get; init; } = [0.95, 1, 1.05];

    public double MinimumScore { get; init; } = 0.78;

    public double MinimumDistinctMargin { get; init; } = 0.025;

    public void Validate()
    {
        if (HorizontalSearchRadius is < 0 or > 256 || VerticalSearchRadius is < 0 or > 256 ||
            SearchStep is < 1 or > 32 || ScaleFactors.Count is < 1 or > 9 ||
            ScaleFactors.Any(scale => scale is < 0.5 or > 2) ||
            MinimumScore is < 0 or > 1 || MinimumDistinctMargin is < 0 or > 0.25)
        {
            throw new ArgumentOutOfRangeException(nameof(HorizontalSearchRadius), "Matcher options are invalid.");
        }
    }
}

public sealed record VisualAnchorMatchResult(
    VisualAnchorMatchStatus Status,
    PixelRect? Bounds,
    double Score,
    double GrayScore,
    double EdgeScore,
    int PhaseIndex,
    int CandidateCount,
    double DistinctMargin)
{
    public bool IsMatch => Status == VisualAnchorMatchStatus.Matched;
}
