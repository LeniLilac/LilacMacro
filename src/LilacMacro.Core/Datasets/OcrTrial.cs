namespace LilacMacro.Core.Datasets;

public sealed record OcrTrial
{
    public required string ModelName { get; init; }

    public string DetectorModelName { get; init; } = string.Empty;

    public string Device { get; init; } = "cpu";

    public required string Text { get; init; }

    public required double Confidence { get; init; }

    public required long ModelLoadMilliseconds { get; init; }

    public required long InferenceMilliseconds { get; init; }

    public required string RuntimeVersion { get; init; }

    public required DateTimeOffset RanAtUtc { get; init; }

    public bool ModelWasCached { get; init; }

    public List<OcrTextRegion> Regions { get; init; } = [];
}
