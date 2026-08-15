using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LilacMacro.Core.Services;

internal static partial class ControlSnapshotJsonReader
{
    private const int MaximumCadenceSeconds = 366 * 24 * 60 * 60;

    public static SignedControlSnapshot Read(JsonElement root)
    {
        RequireObject(root, "keyId", "algorithm", "payload", "signature");
        string keyId = ReadString(root, "keyId", 1, 32);
        if (!KeyIdPattern().IsMatch(keyId)) throw Invalid("key ID");
        string algorithm = ReadString(root, "algorithm", 1, 16);
        if (!string.Equals(algorithm, "Ed25519", StringComparison.Ordinal)) throw Invalid("algorithm");
        ControlPayload payload = ReadPayload(Required(root, "payload"));
        string signature = ReadString(root, "signature", 1, 128);
        return new SignedControlSnapshot(keyId, algorithm, payload, signature);
    }

    private static ControlPayload ReadPayload(JsonElement value)
    {
        RequireObject(
            value,
            "schema",
            "revision",
            "generatedAt",
            "expiresAt",
            "game",
            "codes",
            "schedules",
            "disablements",
            "release");
        if (ReadInt64(value, "schema", 1, 1) != 1) throw Invalid("schema");
        long revision = ReadInt64(value, "revision", 0, long.MaxValue);
        DateTimeOffset generatedAt = ReadDate(value, "generatedAt");
        DateTimeOffset expiresAt = ReadDate(value, "expiresAt");
        ControlGameAvailability game = ReadGame(Required(value, "game"));
        IReadOnlyList<ControlRedeemCode> codes = ReadCodes(Required(value, "codes"));
        IReadOnlyList<ControlSchedule> schedules = ReadSchedules(Required(value, "schedules"));
        IReadOnlyList<ControlDisablement> disablements = ReadDisablements(
            Required(value, "disablements"));
        ControlRelease? release = Required(value, "release").ValueKind == JsonValueKind.Null
            ? null
            : ReadRelease(Required(value, "release"));
        return new ControlPayload(
            revision,
            generatedAt,
            expiresAt,
            game,
            codes,
            schedules,
            disablements,
            release);
    }

    private static ControlGameAvailability ReadGame(JsonElement value)
    {
        RequireObject(
            value,
            "available",
            "operatorAvailable",
            "observedPublic",
            "observedAt",
            "message");
        bool available = ReadBoolean(value, "available");
        bool operatorAvailable = ReadBoolean(value, "operatorAvailable");
        bool? observedPublic = ReadNullableBoolean(value, "observedPublic");
        DateTimeOffset? observedAt = ReadNullableDate(value, "observedAt");
        string? message = ReadNullableText(value, "message", 240);
        if (available != (operatorAvailable && observedPublic is not false))
            throw Invalid("game availability");
        if ((observedPublic is null) != (observedAt is null))
            throw Invalid("game observation");
        return new ControlGameAvailability(
            available,
            operatorAvailable,
            observedPublic,
            observedAt,
            message);
    }

    private static IReadOnlyList<ControlRedeemCode> ReadCodes(JsonElement value)
    {
        RequireArray(value, 100);
        List<ControlRedeemCode> result = [];
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in value.EnumerateArray())
        {
            RequireObject(item, "code", "expiresAt");
            string code = ReadString(item, "code", 1, 64);
            if (!CodePattern().IsMatch(code) || !unique.Add(code)) throw Invalid("redeem code");
            result.Add(new ControlRedeemCode(code, ReadNullableDate(item, "expiresAt")));
        }
        return result;
    }

    private static IReadOnlyList<ControlSchedule> ReadSchedules(JsonElement value)
    {
        RequireArray(value, 20);
        List<ControlSchedule> result = [];
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (JsonElement item in value.EnumerateArray())
        {
            RequireObject(item, "key", "nextAt", "cadenceSeconds");
            string key = ReadString(item, "key", 1, 64);
            if (!ControlScheduleKeys.All.Contains(key) || !unique.Add(key))
                throw Invalid("schedule key");
            result.Add(new ControlSchedule(
                key,
                ReadDate(item, "nextAt"),
                checked((int)ReadInt64(item, "cadenceSeconds", 1, MaximumCadenceSeconds))));
        }
        return result;
    }

    private static IReadOnlyList<ControlDisablement> ReadDisablements(JsonElement value)
    {
        RequireArray(value, ControlFeatureIds.All.Count);
        List<ControlDisablement> result = [];
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (JsonElement item in value.EnumerateArray())
        {
            RequireObject(item, "feature", "reason", "expiresAt");
            string feature = ReadString(item, "feature", 1, 64);
            if (!ControlFeatureIds.All.Contains(feature) || !unique.Add(feature))
                throw Invalid("feature disablement");
            result.Add(new ControlDisablement(
                feature,
                ReadString(item, "reason", 1, 240).Trim(),
                ReadNullableDate(item, "expiresAt")));
        }
        return result;
    }

    private static ControlRelease ReadRelease(JsonElement value)
    {
        RequireObject(value, "version", "pageUrl", "installerUrl", "publishedAt");
        string versionText = ReadString(value, "version", 5, 32);
        if (!VersionPattern().IsMatch(versionText) ||
            !Version.TryParse(versionText, out Version? version) ||
            version.Revision >= 0)
            throw Invalid("release version");
        Uri pageUrl = ReadGitHubUrl(value, "pageUrl", "/LeniLilac/LilacMacro/releases/");
        Uri installerUrl = ReadGitHubUrl(
            value,
            "installerUrl",
            "/LeniLilac/LilacMacro/releases/download/");
        return new ControlRelease(version, pageUrl, installerUrl, ReadDate(value, "publishedAt"));
    }

    private static Uri ReadGitHubUrl(JsonElement owner, string name, string pathPrefix)
    {
        string text = ReadString(owner, name, 1, 2_048);
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal))
            throw Invalid(name);
        return uri;
    }

    private static void RequireObject(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("object");
        HashSet<string> allowed = new(expected, StringComparer.Ordinal);
        HashSet<string> observed = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !observed.Add(property.Name))
                throw Invalid("object properties");
        }
        if (observed.Count != allowed.Count) throw Invalid("object properties");
    }

    private static void RequireArray(JsonElement value, int maximumCount)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximumCount)
            throw Invalid("array");
    }

    private static JsonElement Required(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement value)) throw Invalid(name);
        return value;
    }

    private static string ReadString(JsonElement owner, string name, int minimum, int maximum)
    {
        JsonElement value = Required(owner, name);
        if (value.ValueKind != JsonValueKind.String) throw Invalid(name);
        string text = value.GetString() ?? string.Empty;
        if (text.Length < minimum || text.Length > maximum) throw Invalid(name);
        return text;
    }

    private static string? ReadNullableText(JsonElement owner, string name, int maximum)
    {
        JsonElement value = Required(owner, name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        string text = ReadString(owner, name, 1, maximum).Trim();
        return text.Length == 0 ? throw Invalid(name) : text;
    }

    private static long ReadInt64(JsonElement owner, string name, long minimum, long maximum)
    {
        JsonElement value = Required(owner, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long result) ||
            result < minimum || result > maximum)
            throw Invalid(name);
        return result;
    }

    private static bool ReadBoolean(JsonElement owner, string name)
    {
        JsonElement value = Required(owner, name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid(name),
        };
    }

    private static bool? ReadNullableBoolean(JsonElement owner, string name)
    {
        JsonElement value = Required(owner, name);
        return value.ValueKind == JsonValueKind.Null ? null : ReadBoolean(owner, name);
    }

    private static DateTimeOffset ReadDate(JsonElement owner, string name)
    {
        string text = ReadString(owner, name, 20, 40);
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset result))
            throw Invalid(name);
        return result;
    }

    private static DateTimeOffset? ReadNullableDate(JsonElement owner, string name) =>
        Required(owner, name).ValueKind == JsonValueKind.Null ? null : ReadDate(owner, name);

    private static InvalidDataException Invalid(string field) =>
        new($"Control snapshot {field} was invalid.");

    [GeneratedRegex("^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
