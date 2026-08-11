namespace LilacMacro.Core.Automation;

public enum RobloxPrivateServerLinkKind
{
    ShareCode,
    LegacyLinkCode,
}

public sealed record RobloxPrivateServerLaunchTarget(
    RobloxPrivateServerLinkKind Kind,
    Uri LaunchUri)
{
    private static readonly HashSet<string> AllowedWebHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "roblox.com",
        "www.roblox.com",
        "ro.blox.com",
    };

    public static RobloxPrivateServerLaunchTarget Parse(string? value)
    {
        if (!TryParse(value, out RobloxPrivateServerLaunchTarget? target))
        {
            throw new InvalidDataException(
                "Enter a Roblox private-server share link or legacy privateServerLinkCode link.");
        }
        return target!;
    }

    public static bool TryParse(string? value, out RobloxPrivateServerLaunchTarget? target)
    {
        target = null;
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length == 0 || candidate.Length > 2048) return false;
        if (candidate.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase))
            return TryParseRobloxUri(candidate, out target);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IsAllowedWebHost(uri.Host))
        {
            return false;
        }

        Dictionary<string, string> query = ParseQuery(uri.Query);
        if (TryReadShareCode(query, out string? shareCode))
        {
            target = FromShareCode(shareCode);
            return true;
        }
        if (!TryReadLinkCode(query, out string? linkCode) ||
            !TryReadPlaceId(uri, query, out long placeId))
        {
            return false;
        }
        target = FromLegacyCode(placeId, linkCode);
        return true;
    }

    private static bool TryParseRobloxUri(string candidate, out RobloxPrivateServerLaunchTarget? target)
    {
        target = null;
        if (candidate.StartsWith("roblox://navigation/share_links?", StringComparison.OrdinalIgnoreCase))
        {
            Dictionary<string, string> query = ParseQuery(candidate[(candidate.IndexOf('?') + 1)..]);
            if (!TryReadShareCode(query, out string? shareCode)) return false;
            target = FromShareCode(shareCode);
            return true;
        }
        if (candidate.StartsWith("roblox://experiences/start?", StringComparison.OrdinalIgnoreCase))
        {
            Dictionary<string, string> query = ParseQuery(candidate[(candidate.IndexOf('?') + 1)..]);
            if (!TryReadLinkCode(query, out string? linkCode) || !TryReadPlaceId(query, out long placeId))
                return false;
            target = FromLegacyCode(placeId, linkCode);
            return true;
        }

        const string directPrefix = "roblox://placeId=";
        if (!candidate.StartsWith(directPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        Dictionary<string, string> direct = ParseQuery(candidate["roblox://".Length..]);
        if (!TryReadLinkCode(direct, out string? directLinkCode) ||
            !TryReadPlaceId(direct, out long directPlaceId))
        {
            return false;
        }
        target = FromLegacyCode(directPlaceId, directLinkCode);
        return true;
    }

    private static RobloxPrivateServerLaunchTarget FromShareCode(string code) => new(
        RobloxPrivateServerLinkKind.ShareCode,
        new Uri($"roblox://navigation/share_links?code={Uri.EscapeDataString(code)}&type=Server"));

    private static RobloxPrivateServerLaunchTarget FromLegacyCode(long placeId, string code) => new(
        RobloxPrivateServerLinkKind.LegacyLinkCode,
        new Uri($"roblox://experiences/start?placeId={placeId}&linkCode={Uri.EscapeDataString(code)}"));

    private static bool TryReadShareCode(IReadOnlyDictionary<string, string> query, out string code)
    {
        code = string.Empty;
        if (!query.TryGetValue("code", out string? candidate) ||
            !query.TryGetValue("type", out string? type) ||
            !type.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
            !IsSafeCode(candidate))
        {
            return false;
        }
        code = candidate;
        return true;
    }

    private static bool TryReadLinkCode(IReadOnlyDictionary<string, string> query, out string code)
    {
        code = string.Empty;
        if (!query.TryGetValue("privateServerLinkCode", out string? candidate) &&
            !query.TryGetValue("linkCode", out candidate))
        {
            return false;
        }
        if (!IsSafeCode(candidate)) return false;
        code = candidate;
        return true;
    }

    private static bool TryReadPlaceId(Uri uri, IReadOnlyDictionary<string, string> query, out long placeId)
    {
        if (TryReadPlaceId(query, out placeId)) return true;
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index + 1 < segments.Length; index++)
        {
            if (segments[index].Equals("games", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(segments[index + 1], out placeId) && placeId > 0)
            {
                return true;
            }
        }
        placeId = 0;
        return false;
    }

    private static bool TryReadPlaceId(IReadOnlyDictionary<string, string> query, out long placeId)
    {
        placeId = 0;
        return query.TryGetValue("placeId", out string? value) &&
            long.TryParse(value, out placeId) && placeId > 0;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = Decode(separator < 0 ? pair : pair[..separator]);
            string item = Decode(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            if (key.Length > 0) result[key] = item;
        }
        return result;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private static bool IsSafeCode(string value) =>
        value.Length is >= 4 and <= 256 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsAllowedWebHost(string host) =>
        AllowedWebHosts.Contains(host) || host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase);
}
