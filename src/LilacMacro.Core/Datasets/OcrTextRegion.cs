using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Datasets;

public sealed record OcrTextRegion
{
    public required PixelRect Bounds { get; init; }

    public required string Text { get; init; }

    public double? DetectionConfidence { get; init; }

    public required double RecognitionConfidence { get; init; }
}
