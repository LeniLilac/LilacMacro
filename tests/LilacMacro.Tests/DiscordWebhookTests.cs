using System.Net;
using System.Text;
using LilacMacro.App.Infrastructure;

namespace LilacMacro.Tests;

public sealed class DiscordWebhookTests
{
    private const string DiscordOrigin = "https://discord.com";
    private const string ValidWebhook = DiscordOrigin + "/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456";

    [Fact]
    public async Task Test_delivery_posts_without_enabling_mentions()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));

        await client.SendTestAsync(
            ValidWebhook,
            "Runner 2");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("discord.com", handler.Uri?.Host);
        Assert.Equal("?wait=true&with_components=true", handler.Uri?.Query);
        Assert.Contains("LilacMacro webhook test", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"flags\":32768", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":17", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"parse\":[]", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)DiscordEventKind.RunStarted, "runstarted")]
    [InlineData((int)DiscordEventKind.RunStopped, "runstopped")]
    [InlineData((int)DiscordEventKind.TaskChanged, "taskchanged")]
    [InlineData((int)DiscordEventKind.Victory, "victory")]
    [InlineData((int)DiscordEventKind.Defeat, "defeat")]
    [InlineData((int)DiscordEventKind.Recovery, "recovery")]
    [InlineData((int)DiscordEventKind.TerminalFailure, "terminalfailure")]
    public async Task Event_delivery_attaches_the_roblox_screenshot(
        int kindValue,
        string filenameKind)
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));
        byte[] screenshot = [137, 80, 78, 71, 13, 10, 26, 10];
        DiscordEventKind kind = (DiscordEventKind)kindValue;

        await client.SendEventAsync(
            ValidWebhook,
            new DiscordEventNotification(
                kind,
                "Plan",
                "Task",
                "Event detail.",
                "This desktop",
                DateTimeOffset.UnixEpoch,
                ScreenshotPng: screenshot));

        const string filenamePrefix = "lilacmacro-";
        string filename = $"{filenamePrefix}{filenameKind}-19700101-000000000.png";
        Assert.Equal("multipart/form-data", handler.ContentType);
        Assert.Equal("?wait=true&with_components=true", handler.Uri?.Query);
        Assert.Contains($"attachment://{filename}", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":12", handler.Body, StringComparison.Ordinal);
        Assert.Contains($"\"filename\":\"{filename}\"", handler.Body, StringComparison.Ordinal);
        Assert.True(handler.ContentBytes.Length > screenshot.Length);
    }

    [Fact]
    public async Task Event_boundary_capture_is_queued_once_before_delivery()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));
        TaskCompletionSource<bool> sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.OnRequest = () => sent.TrySetResult(true);
        byte[] screenshot = [137, 80, 78, 71, 13, 10, 26, 10];
        int captures = 0;
        await using DiscordEventDispatcher dispatcher = new(
            () => ValidWebhook,
            _ => { },
            _ =>
            {
                captures++;
                return Task.FromResult<byte[]?>(screenshot);
            },
            client);

        await dispatcher.CaptureAndEnqueueAsync(new DiscordEventNotification(
            DiscordEventKind.Victory,
            "Plan",
            "Task",
            "Victory 1 of 5.",
            "This desktop",
            DateTimeOffset.UnixEpoch));

        Assert.Equal(1, captures);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, captures);
        Assert.Contains("\"type\":12", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Event_boundary_capture_failure_is_not_retried_after_state_changes()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));
        TaskCompletionSource<bool> sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.OnRequest = () => sent.TrySetResult(true);
        int captures = 0;
        await using DiscordEventDispatcher dispatcher = new(
            () => ValidWebhook,
            _ => { },
            _ =>
            {
                captures++;
                throw new InvalidOperationException("capture unavailable");
            },
            client);

        await dispatcher.CaptureAndEnqueueAsync(new DiscordEventNotification(
            DiscordEventKind.RunStarted,
            "Plan",
            null,
            "Started.",
            "This desktop",
            DateTimeOffset.UnixEpoch));

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, captures);
        Assert.DoesNotContain("\"type\":12", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_sends_text_when_screenshot_capture_fails()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));
        TaskCompletionSource<bool> sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.OnRequest = () => sent.TrySetResult(true);
        await using DiscordEventDispatcher dispatcher = new(
            () => ValidWebhook,
            _ => { },
            _ => throw new InvalidOperationException("capture unavailable"),
            client);

        dispatcher.Enqueue(new DiscordEventNotification(
            DiscordEventKind.RunStarted,
            "Plan",
            null,
            "Started.",
            "This desktop",
            DateTimeOffset.UnixEpoch));

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("\"type\":12", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_delivery_mentions_only_the_explicit_user_and_sanitizes_other_mentions()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));

        await client.SendEventAsync(
            ValidWebhook,
            new DiscordEventNotification(
                DiscordEventKind.TerminalFailure,
                "@everyone plan",
                "@here task",
                "Runtime stopped.",
                "This desktop",
                DateTimeOffset.UnixEpoch,
                "123456789012345678"));

        Assert.Contains("\"flags\":32768", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"users\":[\"123456789012345678\"]", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\\u003C@123456789012345678\\u003E", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("@everyone", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("@here", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_terminal_events_cannot_enable_a_configured_mention()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        DiscordWebhookClient client = new(new HttpClient(handler));

        await client.SendEventAsync(
            ValidWebhook,
            new DiscordEventNotification(
                DiscordEventKind.RunStarted,
                "Plan",
                null,
                "Started.",
                "This desktop",
                DateTimeOffset.UnixEpoch,
                "123456789012345678"));

        Assert.Contains("\"users\":[]", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\u003C@123456789012345678\u003E", handler.Body, StringComparison.Ordinal);
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
    [InlineData("http://discord.com" + "/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456")]
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
            DiscordOrigin + $"/api/webhooks/123456789012345678/{secret}",
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
            DiscordOrigin + $"/api/webhooks/123456789012345678/{secret}",
            "Runner 1"));

        Assert.Equal("Discord webhook event could not be delivered.", error.Message);
        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public string? ContentType { get; private set; }
        public byte[] ContentBytes { get; private set; } = [];
        public Action? OnRequest { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            ContentBytes = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Body = Encoding.UTF8.GetString(ContentBytes);
            OnRequest?.Invoke();
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
