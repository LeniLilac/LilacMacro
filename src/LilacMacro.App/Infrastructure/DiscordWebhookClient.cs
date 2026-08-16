using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace LilacMacro.App.Infrastructure;

internal sealed class DiscordWebhookClient
{
    private static readonly HashSet<string> OfficialWebhookHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com", "canary.discord.com", "ptb.discord.com",
    };
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _client;

    public DiscordWebhookClient() : this(SharedClient) { }

    internal DiscordWebhookClient(HttpClient client) => _client = client;

    public Task SendTestAsync(string webhook, string instanceName, CancellationToken cancellationToken = default) =>
        SendAsync(webhook, new DiscordEventNotification(
            DiscordEventKind.RunStarted,
            "Webhook test",
            null,
            "Discord event delivery is ready.",
            instanceName,
            DateTimeOffset.UtcNow), test: true, cancellationToken);

    public Task SendEventAsync(
        string webhook,
        DiscordEventNotification notification,
        CancellationToken cancellationToken = default) =>
        SendAsync(webhook, notification, test: false, cancellationToken);

    private async Task SendAsync(
        string webhook,
        DiscordEventNotification notification,
        bool test,
        CancellationToken cancellationToken)
    {
        Uri destination = Validate(webhook);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        HttpResponseMessage response;
        try
        {
            response = await _client.PostAsJsonAsync(
                destination,
                BuildPayload(notification, test),
                deadline.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException("Discord webhook event could not be delivered.", exception);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Discord rejected the webhook event with HTTP {(int)response.StatusCode}.");
        }
    }

    internal static object BuildPayload(DiscordEventNotification notification, bool test)
    {
        (string icon, string title, int color) = Describe(notification.Kind, test);
        string body = $"**Plan**  {SafeText(notification.Plan)}\n" +
            (string.IsNullOrWhiteSpace(notification.Task) ? string.Empty : $"**Task**  {SafeText(notification.Task)}\n") +
            $"**Instance**  {SafeText(notification.Instance)}\n" +
            $"**Event**  {SafeText(notification.Detail)}\n" +
            $"**Time**  <t:{notification.OccurredAtUtc.ToUnixTimeSeconds()}:R>";
        string? userId = notification.Kind == DiscordEventKind.TerminalFailure &&
            ValidUserId(notification.MentionUserId)
                ? notification.MentionUserId
                : null;
        List<object> children =
        [
            new { type = 10, content = $"### {icon} {title}" },
            new { type = 14, divider = true, spacing = 1 },
            new { type = 10, content = body },
        ];
        if (userId is not null) children.Add(new { type = 10, content = $"<@{userId}>" });

        return new
        {
            flags = 32768,
            components = new[] { new { type = 17, accent_color = color, components = children } },
            allowed_mentions = new
            {
                parse = Array.Empty<string>(),
                users = userId is null ? Array.Empty<string>() : new[] { userId },
            },
        };
    }

    internal static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !OfficialWebhookHosts.Contains(uri.IdnHost)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("Enter an official Discord HTTPS webhook URL.");
        }
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("webhooks", StringComparison.OrdinalIgnoreCase)
            || segments[2].Length is < 1 or > 32
            || !segments[2].All(char.IsAsciiDigit)
            || segments[3].Length is < 20 or > 200
            || segments[3].Any(char.IsControl))
        {
            throw new InvalidDataException("Enter an official Discord webhook URL with its ID and token.");
        }
        return uri;
    }

    private static (string Icon, string Title, int Color) Describe(DiscordEventKind kind, bool test) =>
        test ? ("✓", "LilacMacro webhook test", 0xFF4AA2) : kind switch
        {
            DiscordEventKind.RunStarted => ("▶", "Macro started", 0x4CC9A7),
            DiscordEventKind.RunStopped => ("■", "Macro stopped", 0x8A7F86),
            DiscordEventKind.TaskChanged => ("↻", "Task changed", 0x58A6FF),
            DiscordEventKind.Victory => ("✓", "Run won", 0x4CC9A7),
            DiscordEventKind.Defeat => ("×", "Run lost", 0xFFB13B),
            DiscordEventKind.Recovery => ("↺", "Recovery started", 0xFFB13B),
            DiscordEventKind.TerminalFailure => ("!", "Macro stopped on failure", 0xFF5B6E),
            _ => ("•", "Macro event", 0xFF4AA2),
        };

    private static string SafeText(string? value)
    {
        string safe = new((value ?? string.Empty).Where(character => !char.IsControl(character)).Take(180).ToArray());
        safe = safe.Trim().Replace("@", "＠", StringComparison.Ordinal);
        return Regex.Replace(safe, @"([\\`*_{}\[\]()#+\-.!>|~])", @"\$1");
    }

    private static bool ValidUserId(string? value) =>
        value is not null && value.Length is >= 15 and <= 22 && value.All(char.IsAsciiDigit);

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false });
}
