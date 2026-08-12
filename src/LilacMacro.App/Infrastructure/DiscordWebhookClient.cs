using System.Net.Http;
using System.Net.Http.Json;

namespace LilacMacro.App.Infrastructure;

internal sealed class DiscordWebhookClient
{
    private static readonly HashSet<string> OfficialWebhookHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com",
        "canary.discord.com",
        "ptb.discord.com",
    };
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _client;

    public DiscordWebhookClient() : this(SharedClient) { }

    internal DiscordWebhookClient(HttpClient client) => _client = client;

    public async Task SendTestAsync(
        string webhook,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        Uri destination = Validate(webhook);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        HttpResponseMessage response;
        try
        {
            response = await _client.PostAsJsonAsync(
                destination,
                new
                {
                    content = $"LilacMacro webhook test passed.\nInstance: {Sanitize(instanceName)}",
                    allowed_mentions = new { parse = Array.Empty<string>() },
                },
                deadline.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException("Discord webhook test could not be delivered.", exception);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Discord rejected the webhook test with HTTP {(int)response.StatusCode}.");
        }
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

    private static string Sanitize(string value)
    {
        string result = new(value.Where(character => !char.IsControl(character)).Take(40).ToArray());
        return result.Length == 0 ? "LilacMacro" : result;
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false });
}
