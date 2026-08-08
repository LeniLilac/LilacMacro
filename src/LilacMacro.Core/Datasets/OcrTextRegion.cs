using System.Text.Json.Serialization;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Datasets;

public sealed record OcrTextRegion
{
    public required PixelRect Bounds { get; init; }

    public required string Text { get; init; }

    public double? DetectionConfidence { get; init; }

    public required double RecognitionConfidence { get; init; }

    public bool IsOcrEvidence { get; set; }

    public bool IsVisualAnchor { get; set; }

    public OcrMatchMode MatchMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public OcrEvidenceRole EvidenceRole { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public OcrSpatialSelector SpatialSelector { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SpatialSelectorOverridden { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpatialAnchorText { get; set; }
}
