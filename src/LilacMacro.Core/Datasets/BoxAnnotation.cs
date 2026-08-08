using System.Text.Json.Serialization;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Datasets;

public sealed record BoxAnnotation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? GlobalGroupId { get; set; }

    public required PixelRect Bounds { get; init; }

    public string Label { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MinimumPoolMatches { get; set; }

    public List<OcrTrial> OcrTrials { get; init; } = [];

    [JsonIgnore]
    public bool IsGlobal => GlobalGroupId.HasValue;
}
