using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Vision;

public enum VisualAnchorStrategy
{
    StableAppearance,
    AnimatedAppearance,
    MultiPhase,
    OcrOnly,
}

public enum VisualAnchorClickPoint
{
    TextBoundsCenter,
    TextBoundsTopCenter,
}

public sealed record VisualAnchorDefinition(
    string Id,
    IReadOnlyList<string> TextAliases,
    VisualAnchorClickPoint ClickPoint = VisualAnchorClickPoint.TextBoundsCenter)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128 ||
            Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
        {
            throw new ArgumentException("Anchor id must contain only ASCII letters, digits, dots, dashes, or underscores.", nameof(Id));
        }

        if (TextAliases is null || TextAliases.Count == 0 || TextAliases.Any(string.IsNullOrWhiteSpace) ||
            TextAliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != TextAliases.Count)
        {
            throw new ArgumentException("At least one non-empty OCR alias is required.", nameof(TextAliases));
        }
    }
}

public sealed record VisualAnchorSample(GrayImage Frame, PixelRect Bounds)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Frame);
        if (!Bounds.IsInside(new PixelSize(Frame.Width, Frame.Height)))
        {
            throw new ArgumentOutOfRangeException(nameof(Bounds), "Sample bounds must be inside the frame.");
        }
    }
}

public sealed record VisualFingerprintMetrics(
    double MeanTemporalStandardDeviation,
    double StablePixelRatio,
    double DynamicPixelRatio,
    double EdgeEnergy,
    int PhaseCount);

public sealed record VisualAnchorProfile(
    VisualAnchorDefinition Definition,
    Guid RevisionId,
    DateTimeOffset BuiltAtUtc,
    VisualAnchorStrategy Strategy,
    int ReferenceClientWidth,
    int ReferenceClientHeight,
    int SampleCount,
    int ReferenceBoundsWidth,
    int ReferenceBoundsHeight,
    GrayImage MedianTemplate,
    GrayImage EdgeTemplate,
    GrayImage GrayReliability,
    GrayImage EdgeReliability,
    IReadOnlyList<GrayImage> PhaseTemplates,
    VisualFingerprintMetrics Metrics)
{
    public void Validate()
    {
        Definition.Validate();
        if (RevisionId == Guid.Empty) throw new ArgumentException("Revision id is required.", nameof(RevisionId));
        if (ReferenceClientWidth < 1 || ReferenceClientHeight < 1 || SampleCount < 1 ||
            ReferenceBoundsWidth < 1 || ReferenceBoundsHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ReferenceClientWidth), "Profile dimensions and sample count must be positive.");
        }

        GrayImage[] images = [MedianTemplate, EdgeTemplate, GrayReliability, EdgeReliability, .. PhaseTemplates];
        if (images.Any(image => image.Width != MedianTemplate.Width || image.Height != MedianTemplate.Height))
        {
            throw new ArgumentException("Every profile raster must use the canonical template dimensions.");
        }

        if (!double.IsFinite(Metrics.MeanTemporalStandardDeviation) ||
            Metrics.MeanTemporalStandardDeviation < 0 ||
            Metrics.StablePixelRatio is < 0 or > 1 ||
            Metrics.DynamicPixelRatio is < 0 or > 1 ||
            Metrics.EdgeEnergy is < 0 or > 1 ||
            Metrics.PhaseCount != PhaseTemplates.Count || PhaseTemplates.Count is < 1 or > 8)
        {
            throw new ArgumentException("Visual fingerprint metrics are invalid.", nameof(Metrics));
        }
    }
}
