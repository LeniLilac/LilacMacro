using System.Text.Json.Serialization;

namespace LilacMacro.Core.Datasets;

public sealed record DatasetManifest
{
    public const int CurrentSchemaVersion = 1;

    public const string FormatIdentifier = "lilacmacro.dataset";

    public const string SchemaUri = "https://raw.githubusercontent.com/LeniLilac/LilacMacro/main/schemas/dataset.schema.json";

    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = SchemaUri;

    public string Format { get; init; } = FormatIdentifier;

    public string CoordinateSpace { get; init; } = "roblox_client_pixels_half_open";

    public string ImageRoot { get; init; } = "images";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string SourceWindowTitle { get; init; }

    public required int SourceProcessId { get; init; }

    public required int ClientWidth { get; init; }

    public required int ClientHeight { get; init; }

    public DatasetCaptureMode CaptureMode { get; init; } = DatasetCaptureMode.Timed;

    public required int RequestedFrameCount { get; init; }

    public required double RequestedDurationSeconds { get; init; }

    public bool IsFinalized { get; set; }

    public List<DatasetFrame> Frames { get; init; } = [];
}
