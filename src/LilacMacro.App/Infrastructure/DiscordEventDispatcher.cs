using System.Net.Http;
using System.Threading.Channels;

namespace LilacMacro.App.Infrastructure;

internal sealed class DiscordEventDispatcher : IAsyncDisposable
{
    private readonly DiscordWebhookClient _client;
    private readonly Func<string> _webhook;
    private readonly Action<string> _reportFailure;
    private readonly Channel<DiscordEventNotification> _queue;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;

    public DiscordEventDispatcher(Func<string> webhook, Action<string> reportFailure, DiscordWebhookClient? client = null)
    {
        _webhook = webhook;
        _reportFailure = reportFailure;
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
            try
            {
                await _client.SendEventAsync(webhook, notification, _cancellation.Token)
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
