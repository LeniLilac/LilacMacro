using System.Text.Json.Serialization;

namespace LilacMacro.Core.Vision;

public sealed record VisualProfileManifest
{
    public const int CurrentSchemaVersion = 1;

    public const string FormatIdentifier = "lilacmacro.visual-anchor-profile";

    [JsonPropertyName("$schema")]
    public string Schema { get; init; } =
        "https://raw.githubusercontent.com/LeniLilac/LilacMacro/main/schemas/visual-anchor-profile.schema.json";

    public string Format { get; init; } = FormatIdentifier;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required VisualAnchorDefinition Definition { get; init; }

    public required Guid RevisionId { get; init; }

    public required DateTimeOffset BuiltAtUtc { get; init; }

    public required VisualAnchorStrategy Strategy { get; init; }

    public required int ReferenceClientWidth { get; init; }

    public required int ReferenceClientHeight { get; init; }

    public required int SampleCount { get; init; }

    public required int ReferenceBoundsWidth { get; init; }

    public required int ReferenceBoundsHeight { get; init; }

    public required int CanonicalWidth { get; init; }

    public required int CanonicalHeight { get; init; }

    public required VisualFingerprintMetrics Metrics { get; init; }

    public required IReadOnlyDictionary<string, string> Assets { get; init; }

    public required IReadOnlyDictionary<string, string> Sha256 { get; init; }
}
