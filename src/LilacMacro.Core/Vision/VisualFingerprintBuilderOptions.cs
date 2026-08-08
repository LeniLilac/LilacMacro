namespace LilacMacro.Core.Vision;

public sealed record VisualFingerprintBuilderOptions
{
    public int MinimumSamples { get; init; } = 3;

    public int MaximumCanonicalWidth { get; init; } = 256;

    public int MaximumCanonicalHeight { get; init; } = 128;

    public double StablePixelStandardDeviation { get; init; } = 6;

    public double DynamicPixelStandardDeviation { get; init; } = 12;

    public double PhaseDistanceThreshold { get; init; } = 0.10;

    public int MaximumPhaseTemplates { get; init; } = 4;

    public void Validate()
    {
        if (MinimumSamples < 2 || MaximumCanonicalWidth < 8 || MaximumCanonicalHeight < 8 ||
            StablePixelStandardDeviation <= 0 || DynamicPixelStandardDeviation <= StablePixelStandardDeviation ||
            PhaseDistanceThreshold is <= 0 or >= 1 || MaximumPhaseTemplates is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSamples), "Fingerprint builder options are invalid.");
        }
    }
}
