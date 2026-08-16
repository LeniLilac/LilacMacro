using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Runtime;

internal sealed record PlanShareBundle
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public PlanSettingsSnapshot? Plan { get; init; }

    public List<PlacementSetupDocument> Placements { get; init; } = [];
}

internal static class PlanShareBundleCodec
{
    private const int MaximumCompressedBytes = 180 * 1024;
    private const int MaximumPayloadCharacters = 245_000;
    private const int MaximumDecodedBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static string Encode(PlanShareBundle bundle)
    {
        Validate(bundle);
        using MemoryStream output = new();
        using (BrotliStream compression = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(compression, bundle, JsonOptions);
        if (output.Length > MaximumCompressedBytes)
            throw new InvalidDataException("The selected plan and placements are too large to share together.");
        return Convert.ToBase64String(output.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static PlanShareBundle Decode(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumPayloadCharacters)
            throw new InvalidDataException("The shared configuration payload is invalid.");
        string normalized = payload.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
        byte[] compressed;
        try { compressed = Convert.FromBase64String(normalized); }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The shared configuration payload is invalid.", exception);
        }
        if (compressed.Length > MaximumCompressedBytes)
            throw new InvalidDataException("The shared configuration payload is too large.");

        using MemoryStream input = new(compressed, writable: false);
        using BrotliStream decompression = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = decompression.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (output.Length + read > MaximumDecodedBytes)
                throw new InvalidDataException("The shared configuration expands beyond its safe limit.");
            output.Write(buffer, 0, read);
        }
        PlanShareBundle bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<PlanShareBundle>(output.ToArray(), JsonOptions)
                ?? throw new InvalidDataException("The shared configuration is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The shared configuration JSON is invalid.", exception);
        }
        Validate(bundle);
        return bundle;
    }

    public static void Validate(PlanShareBundle bundle)
    {
        if (bundle.SchemaVersion != PlanShareBundle.CurrentSchemaVersion ||
            bundle.Placements is null || bundle.Placements.Count > PlacementMapCatalog.Definitions.Count ||
            bundle.Plan is null && bundle.Placements.Count == 0)
        {
            throw new InvalidDataException("The shared configuration schema is not supported.");
        }
        if (bundle.Plan is not null &&
            !PlanPersistence.TryRestore([bundle.Plan], out _))
        {
            throw new InvalidDataException("The shared plan is invalid.");
        }
        HashSet<string> mapIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> supported = PlacementMapCatalog.Definitions.Select(map => map.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (PlacementSetupDocument? document in bundle.Placements)
        {
            if (document is null)
                throw new InvalidDataException("The shared placements contain a missing map setup.");
            PlacementSetupRules.Validate(document);
            if (!supported.Contains(document.MapId) || !mapIds.Add(document.MapId))
                throw new InvalidDataException("The shared placements contain an unsupported or duplicate map.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
