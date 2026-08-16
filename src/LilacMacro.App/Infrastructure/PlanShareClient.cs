using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace LilacMacro.App.Infrastructure;

internal sealed class PlanShareClient
{
    private static readonly Uri Endpoint = new("https://macro.expeditions.gg/v1/shares");
    private static readonly Uri ResolveEndpoint = new("https://macro.expeditions.gg/v1/shares/resolve");
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };
    private readonly HttpClient _client;

    public PlanShareClient() : this(SharedClient) { }

    internal PlanShareClient(HttpClient client) => _client = client;

    public async Task<CreatedPlanShare> CreateAsync(string payload, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { payload }),
        };
        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        CreatedPlanShare created = await response.Content.ReadFromJsonAsync<CreatedPlanShare>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The sharing service returned an empty response.");
        return created with { Code = NormalizeCode(created.Code) };
    }

    public async Task<FetchedPlanShare> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        code = NormalizeCode(code);
        using HttpRequestMessage request = new(HttpMethod.Post, ResolveEndpoint)
        {
            Content = JsonContent.Create(new { code }),
        };
        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<FetchedPlanShare>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The sharing service returned an empty response.");
    }

    internal static string NormalizeCode(string value)
    {
        string code = value.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        if (code.Length != 20 || code.Any(character => !"23456789ABCDEFGHJKMNPQRSTUVWXYZ".Contains(character)))
            throw new InvalidDataException("Enter a 20-character share code.");
        return code;
    }

    internal static string FormatCode(string value)
    {
        string code = NormalizeCode(value);
        return string.Join('-', Enumerable.Range(0, 4).Select(index => code.Substring(index * 5, 5)));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            await response.Content.LoadIntoBufferAsync(256 * 1024, cancellationToken).ConfigureAwait(false);
            return;
        }
        string message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => "That share code was not found or has expired.",
            System.Net.HttpStatusCode.TooManyRequests => "Sharing is temporarily rate limited. Try again later.",
            _ => "The sharing service could not complete the request.",
        };
        throw new HttpRequestException(message);
    }
}

internal sealed record CreatedPlanShare(string Code, DateTimeOffset ExpiresAt);

internal sealed record FetchedPlanShare(string Payload, DateTimeOffset ExpiresAt);
