using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.Tests;

public sealed class ControlSnapshotPollingTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "LilacMacro.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Transport_accepts_only_bounded_json_from_exact_endpoint()
    {
        using HttpClient client = CreateClient(request => JsonResponse("{}", request));
        using ControlSnapshotTransport transport = new(
            client,
            ownsClient: false,
            TimeSpan.FromSeconds(1));

        ReadOnlyMemory<byte> response = await transport.GetAsync();

        Assert.Equal("{}", Encoding.UTF8.GetString(response.Span));
    }

    [Fact]
    public async Task Transport_rejects_redirect_wrong_origin_content_type_and_size()
    {
        await AssertTransportFailure(request => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            RequestMessage = request,
            Headers = { Location = new Uri("https://example.com/control") },
        });
        await AssertTransportFailure(_ => JsonResponse(
            "{}",
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/control")));
        await AssertTransportFailure(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent("{}"),
        });
        await AssertTransportFailure(request =>
        {
            HttpResponseMessage response = JsonResponse("{}", request);
            response.Content.Headers.ContentLength = ControlSnapshotVerifier.MaximumSnapshotBytes + 1;
            return response;
        });
    }

    [Fact]
    public async Task Transport_bounds_streams_without_a_declared_length()
    {
        using HttpClient client = CreateClient(request =>
        {
            StreamContent content = new(new MemoryStream(
                new byte[ControlSnapshotVerifier.MaximumSnapshotBytes + 1]));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
        });
        using ControlSnapshotTransport transport = new(
            client,
            ownsClient: false,
            TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidDataException>(() => transport.GetAsync());
    }

    [Fact]
    public async Task Transport_timeout_is_bounded_and_caller_cancellation_propagates()
    {
        using HttpClient client = new(new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }))
        { Timeout = Timeout.InfiniteTimeSpan };
        using ControlSnapshotTransport transport = new(
            client,
            ownsClient: false,
            TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.GetAsync());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.GetAsync(cancellation.Token));
    }

    [Fact]
    public async Task Poller_publishes_valid_snapshot_and_retains_fresh_last_known_good()
    {
        MutableTimeProvider time = new(ControlSnapshotTests.FixtureNow);
        QueueTransport transport = new(
            Encoding.UTF8.GetBytes(ControlSnapshotTests.FixtureJson),
            new HttpRequestException("offline"));
        ControlSnapshotPollingService poller = CreatePoller(transport, time);

        ControlPollResult first = await poller.RefreshAsync();
        ControlPollResult second = await poller.RefreshAsync();

        Assert.Equal(ControlPollState.Fresh, first.State);
        Assert.Equal(ControlPollState.OfflineUsingLastKnownGood, second.State);
        Assert.Equal(42, poller.RevisionFloor);
        Assert.Equal(42, poller.Current?.Payload.Revision);
    }

    [Fact]
    public async Task Poller_never_applies_expired_last_known_good()
    {
        MutableTimeProvider time = new(ControlSnapshotTests.FixtureNow);
        QueueTransport transport = new(
            Encoding.UTF8.GetBytes(ControlSnapshotTests.FixtureJson),
            new HttpRequestException("offline"));
        ControlSnapshotPollingService poller = CreatePoller(transport, time);

        await poller.RefreshAsync();
        time.Advance(TimeSpan.FromMinutes(6));
        ControlPollResult result = await poller.RefreshAsync();

        Assert.Equal(ControlPollState.OfflineWithoutSnapshot, result.State);
        Assert.Null(result.Snapshot);
        Assert.Null(poller.Current);
        Assert.Equal(42, poller.RevisionFloor);
    }

    [Fact]
    public async Task Poller_rejects_tampering_without_discarding_fresh_last_known_good()
    {
        MutableTimeProvider time = new(ControlSnapshotTests.FixtureNow);
        byte[] valid = Encoding.UTF8.GetBytes(ControlSnapshotTests.FixtureJson);
        byte[] tampered = Encoding.UTF8.GetBytes(ControlSnapshotTests.FixtureJson.Replace(
            "\"revision\":42",
            "\"revision\":43",
            StringComparison.Ordinal));
        ControlSnapshotPollingService poller = CreatePoller(
            new QueueTransport(valid, tampered),
            time);

        await poller.RefreshAsync();
        ControlPollResult rejected = await poller.RefreshAsync();

        Assert.Equal(ControlPollState.RejectedUsingLastKnownGood, rejected.State);
        Assert.Equal(42, rejected.Snapshot?.Payload.Revision);
        Assert.Equal(42, poller.RevisionFloor);
    }

    [Fact]
    public async Task Poller_recovers_signed_cache_but_keeps_stale_cache_inactive()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "control.json");
        await File.WriteAllTextAsync(path, ControlSnapshotTests.FixtureJson);
        MutableTimeProvider freshTime = new(ControlSnapshotTests.FixtureNow);
        ControlSnapshotPollingService fresh = CreatePoller(
            new QueueTransport(new HttpRequestException("offline")),
            freshTime,
            path);

        ControlPollResult freshResult = await fresh.RefreshAsync();
        Assert.Equal(ControlPollState.OfflineUsingLastKnownGood, freshResult.State);

        MutableTimeProvider staleTime = new(ControlSnapshotTests.FixtureNow + TimeSpan.FromMinutes(6));
        ControlSnapshotPollingService stale = CreatePoller(
            new QueueTransport(new HttpRequestException("offline")),
            staleTime,
            path);
        ControlPollResult staleResult = await stale.RefreshAsync();

        Assert.Equal(ControlPollState.OfflineWithoutSnapshot, staleResult.State);
        Assert.Null(stale.Current);
        Assert.Equal(42, stale.RevisionFloor);
    }

    [Fact]
    public void Production_trust_material_is_a_valid_Ed25519_key()
    {
        ControlSnapshotVerifier verifier = new(ControlSnapshotTrust.PublicKeys);
        Assert.NotNull(verifier);
        Assert.Equal("https://macro.expeditions.gg/v1/control", ControlSnapshotTrust.Endpoint.AbsoluteUri);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private ControlSnapshotPollingService CreatePoller(
        IControlSnapshotTransport transport,
        TimeProvider time,
        string? path = null) => new(
            transport,
            new ControlSnapshotStore(
                path ?? Path.Combine(_directory, "control.json"),
                ControlSnapshotTests.CreateVerifier()),
            time,
            () => TimeSpan.Zero);

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new DelegateHandler((request, _) => Task.FromResult(response(request))))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

    private static HttpResponseMessage JsonResponse(string json, HttpRequestMessage request)
    {
        StringContent content = new(json, Encoding.UTF8, "application/json");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = content,
        };
    }

    private static async Task AssertTransportFailure(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        using HttpClient client = CreateClient(response);
        using ControlSnapshotTransport transport = new(
            client,
            ownsClient: false,
            TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<Exception>(() => transport.GetAsync());
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class QueueTransport(params object[] outcomes) : IControlSnapshotTransport
    {
        private readonly Queue<object> _outcomes = new(outcomes);

        public Task<ReadOnlyMemory<byte>> GetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object outcome = _outcomes.Dequeue();
            return outcome switch
            {
                byte[] bytes => Task.FromResult<ReadOnlyMemory<byte>>(bytes),
                Exception exception => Task.FromException<ReadOnlyMemory<byte>>(exception),
                _ => throw new InvalidOperationException(),
            };
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
