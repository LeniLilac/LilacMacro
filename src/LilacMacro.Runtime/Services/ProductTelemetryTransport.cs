using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LilacMacro.Core.Services;

namespace LilacMacro.Runtime.Services;

public sealed class ProductTelemetryTransport : IProductTelemetryTransport, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public ProductTelemetryTransport() : this(CreateClient(), ownsClient: true) { }

    internal ProductTelemetryTransport(HttpClient client, bool ownsClient)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
    }

    public async Task SendAsync(
        ProductTelemetryBatch batch,
        CancellationToken cancellationToken = default)
    {
        ProductTelemetryPolicy.Validate(batch);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(batch, JsonOptions);
        if (body.Length > ProductTelemetryPolicy.MaximumRequestBytes)
            throw new InvalidDataException("Telemetry request exceeded its size bound.");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using HttpRequestMessage request = new(HttpMethod.Post, ProductTelemetryPolicy.Endpoint)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.UserAgent.ParseAdd("LilacMacro-Telemetry/1.0");
        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri != ProductTelemetryPolicy.Endpoint || IsRedirect(response.StatusCode))
            throw new HttpRequestException("The telemetry endpoint attempted an untrusted redirect.");
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException("The telemetry endpoint returned an unsuccessful status.");
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
    { Timeout = Timeout.InfiniteTimeSpan };

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;
}
