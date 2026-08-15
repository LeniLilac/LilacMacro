using System.Net;
using System.Net.Http.Headers;
using LilacMacro.Core.Services;

namespace LilacMacro.Runtime.Services;

public interface IControlSnapshotTransport
{
    Task<ReadOnlyMemory<byte>> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class ControlSnapshotTransport : IControlSnapshotTransport, IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _requestTimeout;

    public ControlSnapshotTransport()
        : this(CreateClient(), ownsClient: true, DefaultRequestTimeout) { }

    internal ControlSnapshotTransport(
        HttpClient client,
        bool ownsClient,
        TimeSpan requestTimeout)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (requestTimeout <= TimeSpan.Zero || requestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _ownsClient = ownsClient;
        _requestTimeout = requestTimeout;
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        using HttpRequestMessage request = new(HttpMethod.Get, ControlSnapshotTrust.Endpoint)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.UserAgent.ParseAdd("LilacMacro-Control/1.0");

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri != ControlSnapshotTrust.Endpoint)
            throw new HttpRequestException("The control response did not come from the trusted endpoint.");
        if (IsRedirect(response.StatusCode))
            throw new HttpRequestException("The control endpoint attempted an untrusted redirect.");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("The control endpoint returned an unsuccessful status.");
        if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The control response content type was invalid.");
        if (response.Content.Headers.ContentLength is long declared &&
            (declared < 2 || declared > ControlSnapshotVerifier.MaximumSnapshotBytes))
            throw new InvalidDataException("The control response declared an invalid size.");

        await using Stream source = await response.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        using MemoryStream destination = new();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > ControlSnapshotVerifier.MaximumSnapshotBytes)
                throw new InvalidDataException("The control response exceeded its size bound.");
            await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token)
                .ConfigureAwait(false);
        }
        if (destination.Length < 2)
            throw new InvalidDataException("The control response was empty.");
        return destination.ToArray();
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;
}
