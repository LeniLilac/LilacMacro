using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace LilacMacro.App.Updates;

internal sealed class UpdateHttpTransport : IDisposable
{
    private const int MaximumRedirects = 5;
    private readonly HttpClient client;
    private readonly bool ownsClient;

    public UpdateHttpTransport(HttpClient? client = null)
    {
        ownsClient = client is null;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
    }

    public async Task<byte[]> GetBytesAsync(Uri uri, long maximumBytes, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetResponseAsync(uri, cancellationToken).ConfigureAwait(false);
        ValidateLength(response, maximumBytes);
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream destination = new();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes) throw new InvalidDataException("The update response exceeded its trusted size bound.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return destination.ToArray();
    }

    public async Task<string> DownloadAsync(
        Uri uri,
        string destinationPath,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetResponseAsync(uri, cancellationToken).ConfigureAwait(false);
        ValidateLength(response, expectedBytes);
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > expectedBytes) throw new InvalidDataException("The update asset exceeded its declared size.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (total != expectedBytes) throw new InvalidDataException("The update asset size did not match GitHub metadata.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private async Task<HttpResponseMessage> GetResponseAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        Uri current = initialUri;
        for (int redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            ValidateUri(current, redirect > 0);
            using HttpRequestMessage request = new(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("LilacMacro-Updater/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
            {
                response.EnsureSuccessStatusCode();
                return response;
            }
            if (redirect == MaximumRedirects)
            {
                response.Dispose();
                throw new HttpRequestException("The update download exceeded its redirect limit.");
            }
            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new HttpRequestException("The update redirect did not provide a destination.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
        throw new HttpRequestException("The update request could not be completed.");
    }

    internal static void ValidateUri(Uri uri, bool redirected)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("The update URL is not trusted HTTPS.");
        string host = uri.IdnHost;
        bool trusted = host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || redirected && (host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
        if (!trusted) throw new InvalidDataException("The update URL host is not trusted.");
    }

    private static bool IsRedirect(HttpStatusCode code) => code is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static void ValidateLength(HttpResponseMessage response, long maximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidDataException("The update response declared an excessive size.");
    }

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
    }
}
