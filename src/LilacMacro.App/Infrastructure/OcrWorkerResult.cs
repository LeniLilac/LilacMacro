using System.Text.Json.Serialization;

namespace LilacMacro.App.Infrastructure;

public sealed record OcrWorkerResult
{
    [JsonPropertyName("model_name")]
    public required string ModelName { get; init; }

    [JsonPropertyName("detector_model_name")]
    public required string DetectorModelName { get; init; }

    [JsonPropertyName("device")]
    public required string Device { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("model_load_milliseconds")]
    public required long ModelLoadMilliseconds { get; init; }

    [JsonPropertyName("inference_milliseconds")]
    public required long InferenceMilliseconds { get; init; }

    [JsonPropertyName("paddleocr_version")]
    public required string PaddleOcrVersion { get; init; }

    [JsonPropertyName("model_cached")]
    public bool ModelCached { get; init; }

    [JsonPropertyName("regions")]
    public OcrWorkerRegion[] Regions { get; init; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public sealed record OcrWorkerRegion
{
    [JsonPropertyName("x")]
    public required int X { get; init; }

    [JsonPropertyName("y")]
    public required int Y { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("detection_confidence")]
    public double? DetectionConfidence { get; init; }

    [JsonPropertyName("recognition_confidence")]
    public required double RecognitionConfidence { get; init; }
}
