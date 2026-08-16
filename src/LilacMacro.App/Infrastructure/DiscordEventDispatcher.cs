using System.Net.Http;
using System.Threading.Channels;

namespace LilacMacro.App.Infrastructure;

internal sealed class DiscordEventDispatcher : IAsyncDisposable
{
    private readonly DiscordWebhookClient _client;
    private readonly Func<string> _webhook;
    private readonly Action<string> _reportFailure;
    private readonly Func<CancellationToken, Task<byte[]?>>? _captureScreenshot;
    private readonly Channel<DiscordEventNotification> _queue;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;

    public DiscordEventDispatcher(
        Func<string> webhook,
        Action<string> reportFailure,
        Func<CancellationToken, Task<byte[]?>>? captureScreenshot = null,
        DiscordWebhookClient? client = null)
    {
        _webhook = webhook;
        _reportFailure = reportFailure;
        _captureScreenshot = captureScreenshot;
        _client = client ?? new DiscordWebhookClient();
        _queue = Channel.CreateBounded<DiscordEventNotification>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        _worker = RunAsync();
    }

    public void Enqueue(DiscordEventNotification notification)
    {
        if (!string.IsNullOrWhiteSpace(_webhook())) _ = _queue.Writer.TryWrite(notification);
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _cancellation.Cancel();
        try { await _worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        await foreach (DiscordEventNotification notification in
                       _queue.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
        {
            string webhook = _webhook();
            if (string.IsNullOrWhiteSpace(webhook)) continue;
            byte[]? screenshot = null;
            if (_captureScreenshot is not null)
            {
                try
                {
                    screenshot = await _captureScreenshot(_cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // A missing frame must not suppress the text event or interrupt the macro.
                }
            }
            try
            {
                await _client.SendEventAsync(
                        webhook,
                        notification with { ScreenshotPng = screenshot },
                        _cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or TaskCanceledException)
            {
                _reportFailure(exception is TaskCanceledException
                    ? "Discord event delivery timed out."
                    : exception.Message);
            }
        }
    }
}
