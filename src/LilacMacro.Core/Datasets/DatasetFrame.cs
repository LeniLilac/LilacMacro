namespace LilacMacro.Core.Datasets;

public sealed record DatasetFrame
{
    public required string FileName { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required string Sha256 { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public FrameVerdict Verdict { get; set; }

    public string Notes { get; set; } = string.Empty;

    public List<BoxAnnotation> Annotations { get; init; } = [];
}
