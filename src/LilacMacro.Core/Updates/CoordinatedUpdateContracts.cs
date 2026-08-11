using System.Globalization;

namespace LilacMacro.Core.Updates;

public sealed record CoordinatedUpdateState(
    Guid OperationId,
    LilacSemanticVersion TargetVersion,
    string InstallerSha256,
    string RequestPath,
    IReadOnlyList<int> ParticipantProcessIds,
    IReadOnlyList<string> ActiveRunnerIds);

public sealed record CoordinatedUpdateRequest(
    Guid OperationId,
    LilacSemanticVersion TargetVersion,
    DateTimeOffset RequestedAtUtc);

public static class CoordinatedUpdateText
{
    public const int SchemaVersion = 1;

    public static string SerializeState(CoordinatedUpdateState state)
    {
        ValidateState(state);
        List<string> lines =
        [
            $"schema_version={SchemaVersion}",
            $"operation_id={state.OperationId:D}",
            $"target_version={state.TargetVersion}",
            $"installer_sha256={state.InstallerSha256.ToUpperInvariant()}",
            $"request_path={state.RequestPath}",
        ];
        lines.AddRange(state.ParticipantProcessIds.Distinct().Order().Select(pid => $"participant_pid={pid}"));
        lines.AddRange(state.ActiveRunnerIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(id => $"active_runner={id}"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static CoordinatedUpdateState ParseState(string text)
    {
        IReadOnlyList<KeyValuePair<string, string>> fields = ParseFields(text);
        RequireSchema(fields);
        CoordinatedUpdateState state = new(
            ParseGuid(Single(fields, "operation_id")),
            ParseVersion(Single(fields, "target_version")),
            ParseSha256(Single(fields, "installer_sha256")),
            Single(fields, "request_path"),
            fields.Where(field => field.Key == "participant_pid").Select(field => ParsePid(field.Value)).ToArray(),
            fields.Where(field => field.Key == "active_runner").Select(field => ParseRunnerId(field.Value)).ToArray());
        ValidateState(state);
        return state;
    }

    public static string SerializeRequest(CoordinatedUpdateRequest request) => string.Join(Environment.NewLine,
    [
        $"schema_version={SchemaVersion}",
        $"operation_id={request.OperationId:D}",
        $"target_version={request.TargetVersion}",
        $"requested_utc={request.RequestedAtUtc.ToUniversalTime():O}",
        string.Empty,
    ]);

    public static CoordinatedUpdateRequest ParseRequest(string text)
    {
        IReadOnlyList<KeyValuePair<string, string>> fields = ParseFields(text);
        RequireSchema(fields);
        return new CoordinatedUpdateRequest(
            ParseGuid(Single(fields, "operation_id")),
            ParseVersion(Single(fields, "target_version")),
            DateTimeOffset.ParseExact(
                Single(fields, "requested_utc"),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
    }

    public static bool ShouldClose(
        CoordinatedUpdateRequest request,
        LilacSemanticVersion currentVersion,
        DateTimeOffset nowUtc) =>
        request.TargetVersion.CompareTo(currentVersion) > 0
        && request.RequestedAtUtc <= nowUtc.AddMinutes(1)
        && request.RequestedAtUtc >= nowUtc.AddMinutes(-10);

    private static IReadOnlyList<KeyValuePair<string, string>> ParseFields(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 16 * 1024) throw new InvalidDataException("The coordinated update record is too large.");
        List<KeyValuePair<string, string>> fields = [];
        foreach (string rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.Length == 0) continue;
            int equals = rawLine.IndexOf('=');
            if (equals <= 0 || equals == rawLine.Length - 1)
                throw new InvalidDataException("The coordinated update record is malformed.");
            string key = rawLine[..equals];
            string value = rawLine[(equals + 1)..];
            if (!key.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')
                || value.Contains('\r') || value.Contains('\n'))
            {
                throw new InvalidDataException("The coordinated update record contains invalid text.");
            }
            fields.Add(new KeyValuePair<string, string>(key, value));
        }
        return fields;
    }

    private static void ValidateState(CoordinatedUpdateState state)
    {
        if (state.OperationId == Guid.Empty) throw new InvalidDataException("The update operation id is missing.");
        _ = ParseSha256(state.InstallerSha256);
        if (string.IsNullOrWhiteSpace(state.RequestPath) || state.RequestPath.Contains('\r') || state.RequestPath.Contains('\n'))
            throw new InvalidDataException("The update request path is invalid.");
        if (state.ParticipantProcessIds.Count is < 1 or > 64 || state.ParticipantProcessIds.Any(pid => pid <= 0))
            throw new InvalidDataException("The update participant list is invalid.");
        if (state.ActiveRunnerIds.Count > 16) throw new InvalidDataException("Too many active runners were recorded.");
        foreach (string runnerId in state.ActiveRunnerIds) _ = ParseRunnerId(runnerId);
    }

    private static void RequireSchema(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        if (Single(fields, "schema_version") != SchemaVersion.ToString(CultureInfo.InvariantCulture))
            throw new InvalidDataException("The coordinated update schema is unsupported.");
    }

    private static string Single(IReadOnlyList<KeyValuePair<string, string>> fields, string key)
    {
        string[] values = fields.Where(field => field.Key == key).Select(field => field.Value).ToArray();
        return values.Length == 1 ? values[0] : throw new InvalidDataException($"The coordinated update field {key} is missing or duplicated.");
    }

    private static Guid ParseGuid(string value) => Guid.TryParseExact(value, "D", out Guid result) && result != Guid.Empty
        ? result
        : throw new InvalidDataException("The update operation id is invalid.");

    private static LilacSemanticVersion ParseVersion(string value) => LilacSemanticVersion.TryParse(value, out LilacSemanticVersion result)
        ? result
        : throw new InvalidDataException("The update version is invalid.");

    private static string ParseSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit)
        ? value.ToUpperInvariant()
        : throw new InvalidDataException("The update installer digest is invalid.");

    private static int ParsePid(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && pid > 0
        ? pid
        : throw new InvalidDataException("An update participant process id is invalid.");

    private static string ParseRunnerId(string value) => value.Length is > 0 and <= 32
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        ? value
        : throw new InvalidDataException("An active runner identifier is invalid.");
}
