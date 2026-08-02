using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Datasets;

public sealed record BoxAnnotation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required PixelRect Bounds { get; init; }

    public string Label { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public List<OcrTrial> OcrTrials { get; init; } = [];
}
