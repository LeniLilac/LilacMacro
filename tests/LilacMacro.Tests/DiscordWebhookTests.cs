using System.Net;
using System.Text;
using LilacMacro.App.Infrastructure;

namespace LilacMacro.Tests;

public sealed class DiscordWebhookTests
{
    [Fact]
    public async Task Test_delivery_posts_without_enabling_mentions()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));

        await client.SendTestAsync(
            "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456",
            "Runner 2");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("discord.com", handler.Uri?.Host);
        Assert.Contains("LilacMacro webhook test passed", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"parse\":[]", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("discord.com")]
    [InlineData("canary.discord.com")]
    [InlineData("ptb.discord.com")]
    public void Official_discord_clients_are_accepted(string host)
    {
        Uri result = DiscordWebhookClient.Validate(
            $"https://{host}/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456");

        Assert.Equal(host, result.Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("https://example.test/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("https://canary.discord.com.example.test/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("https://ptb-discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("https://discord.com/channels/123/456")]
    public void Test_delivery_rejects_untrusted_destinations(string value) =>
        Assert.Throws<InvalidDataException>(() => DiscordWebhookClient.Validate(value));

    [Fact]
    public async Task Discord_failure_does_not_echo_the_secret_url()
    {
        RecordingHandler handler = new(HttpStatusCode.Unauthorized);
        DiscordWebhookClient client = new(new HttpClient(handler));
        const string secret = "abcdefghijklmnopqrstuvwxyz123456";

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendTestAsync(
            $"https://discord.com/api/webhooks/123456789012345678/{secret}",
            "This desktop"));

        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
        Assert.Contains("HTTP 401", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_failure_does_not_echo_the_secret_url()
    {
        const string secret = "abcdefghijklmnopqrstuvwxyz123456";
        DiscordWebhookClient client = new(new HttpClient(new ThrowingHandler(secret)));

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendTestAsync(
            $"https://discord.com/api/webhooks/123456789012345678/{secret}",
            "Runner 1"));

        Assert.Equal("Discord webhook test could not be delivered.", error.Message);
        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8),
            };
        }
    }

    private sealed class ThrowingHandler(string secret) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException($"simulated transport failure for {secret}");
    }
}
