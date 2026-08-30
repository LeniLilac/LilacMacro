using System.Text.RegularExpressions;

namespace LilacMacro.App.Diagnostics;

internal static partial class DeepDebugRedactor
{
    private const string RedactedUser = "[REDACTED WINDOWS USER]";

    [GeneratedRegex("https://(?:[a-z0-9-]+\\.)?discord(?:app)?\\.com/api(?:/v\\d+)?/webhooks/[^\\s\\\"'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiscordWebhookPattern();

    [GeneratedRegex("(?:https://(?:[a-z0-9-]+\\.)?roblox\\.com/[^\\s\\\"'<>]*(?:privateServerLinkCode|linkCode|[?&]code=)[^\\s\\\"'<>]*|roblox://[^\\s\\\"'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RobloxPrivateServerPattern();

    [GeneratedRegex("(?<prefix>[a-z]:[\\\\/]+(?:users|documents and settings)[\\\\/]+)(?<user>[^\\\\/\\s\\\"'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsProfilePathPattern();

    public static string Redact(string text) => Redact(text, Environment.UserName);

    internal static string Redact(string text, string? windowsUser)
    {
        string redacted = DiscordWebhookPattern().Replace(text, "[REDACTED DISCORD WEBHOOK]");
        redacted = RobloxPrivateServerPattern().Replace(redacted, "[REDACTED ROBLOX PRIVATE SERVER LINK]");
        redacted = WindowsProfilePathPattern().Replace(
            redacted,
            match => $"{match.Groups["prefix"].Value}{RedactedUser}");
        return !IsSafeBareUserToken(windowsUser)
            ? redacted
            : redacted.Replace(windowsUser!, RedactedUser, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeBareUserToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= 3 &&
        value.Any(char.IsLetter);
}
