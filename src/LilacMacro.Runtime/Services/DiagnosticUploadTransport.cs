using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LilacMacro.Core.Services;

namespace LilacMacro.Runtime.Services;

public interface IDiagnosticUploadTransport
{
    Task<DiagnosticUploadResult> UploadAsync(
        string archivePath,
        DiagnosticArchiveKind kind,
        string appVersion,
        Guid installId,
        IProgress<DiagnosticUploadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class DiagnosticUploadTransport : IDiagnosticUploadTransport, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _api;
    private readonly HttpClient _storage;
    private readonly bool _ownsClients;

    public DiagnosticUploadTransport()
        : this(CreateClient(), CreateClient(), ownsClients: true) { }

    internal DiagnosticUploadTransport(
        HttpClient api,
        HttpClient storage,
        bool ownsClients)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _ownsClients = ownsClients;
    }

    public async Task<DiagnosticUploadResult> UploadAsync(
        string archivePath,
        DiagnosticArchiveKind kind,
        string appVersion,
        Guid installId,
        IProgress<DiagnosticUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (installId == Guid.Empty)
            throw new InvalidDataException("Diagnostic installation identity is invalid.");
        if (!Version.TryParse(appVersion, out Version? parsedVersion) ||
            parsedVersion.Major < 0 || parsedVersion.Build < 0)
        {
            throw new InvalidDataException("Application version is invalid.");
        }

        FileInfo archive = new(archivePath);
        if (!archive.Exists) throw new FileNotFoundException("Diagnostic archive was not found.");
        string fileName = DiagnosticUploadPolicy.ValidateArchive(archive.FullName, archive.Length);
        progress?.Report(new(DiagnosticUploadPhase.Preparing, 0, archive.Length));
        string sha256 = await HashSegmentAsync(
            archive.FullName,
            0,
            archive.Length,
            bytes => progress?.Report(new(
                DiagnosticUploadPhase.Hashing,
                bytes,
                archive.Length)),
            cancellationToken).ConfigureAwait(false);

        CreateUploadResponse grant = await SendApiAsync<CreateUploadRequest, CreateUploadResponse>(
            HttpMethod.Post,
            DiagnosticUploadPolicy.CreateEndpoint,
            new CreateUploadRequest(
                installId,
                fileName,
                archive.Length,
                sha256,
                DiagnosticUploadPolicy.KindValue(kind),
                true,
                parsedVersion.ToString(3)),
            null,
            cancellationToken).ConfigureAwait(false);
        ValidateGrant(grant);

        if (string.Equals(grant.Upload.Kind, "single", StringComparison.Ordinal))
        {
            await UploadSingleAsync(
                archive.FullName,
                archive.Length,
                sha256,
                grant.Upload,
                progress,
                cancellationToken).ConfigureAwait(false);
            await CompleteAsync(grant, [], cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(grant.Upload.Kind, "multipart", StringComparison.Ordinal))
        {
            IReadOnlyList<CompletedPart> parts = await UploadMultipartAsync(
                archive.FullName,
                archive.Length,
                grant,
                progress,
                cancellationToken).ConfigureAwait(false);
            await CompleteAsync(grant, parts, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidDataException("Diagnostic upload mode was invalid.");
        }

        progress?.Report(new(DiagnosticUploadPhase.Finalizing, archive.Length, archive.Length));
        progress?.Report(new(DiagnosticUploadPhase.Complete, archive.Length, archive.Length));
        return new DiagnosticUploadResult(
            grant.Id,
            "Verifying",
            ParseTimestamp(grant.ExpiresAt));
    }

    public void Dispose()
    {
        if (!_ownsClients) return;
        _api.Dispose();
        _storage.Dispose();
    }

    private async Task UploadSingleAsync(
        string path,
        long length,
        string sha256,
        UploadDescriptor upload,
        IProgress<DiagnosticUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Uri uri = TrustedStorageUri(upload.Url);
        IReadOnlyDictionary<string, string> headers = ValidateHeaders(
            upload.RequiredHeaders,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["content-type"] = "application/zip",
                ["x-amz-checksum-sha256"] = ChecksumHeader(sha256),
                ["x-amz-server-side-encryption"] = "AES256",
            });
        await using FileStream file = OpenArchive(path);
        using StreamContent content = new(file);
        content.Headers.ContentLength = length;
        content.Headers.ContentType = new MediaTypeHeaderValue(headers["content-type"]);
        using HttpRequestMessage request = new(HttpMethod.Put, uri) { Content = content };
        request.Headers.TryAddWithoutValidation(
            "x-amz-checksum-sha256",
            headers["x-amz-checksum-sha256"]);
        request.Headers.TryAddWithoutValidation(
            "x-amz-server-side-encryption",
            headers["x-amz-server-side-encryption"]);
        progress?.Report(new(DiagnosticUploadPhase.Uploading, 0, length));
        await SendStorageAsync(request, cancellationToken).ConfigureAwait(false);
        progress?.Report(new(DiagnosticUploadPhase.Uploading, length, length));
    }

    private async Task<IReadOnlyList<CompletedPart>> UploadMultipartAsync(
        string path,
        long totalBytes,
        CreateUploadResponse grant,
        IProgress<DiagnosticUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        int partSize = grant.Upload.PartSizeBytes
            ?? throw new InvalidDataException("Multipart part size was missing.");
        int partCount = grant.Upload.PartCount
            ?? throw new InvalidDataException("Multipart part count was missing.");
        int expectedParts = checked((int)((totalBytes + partSize - 1) / partSize));
        if (partSize is <= 0 or > 256 * 1024 * 1024 ||
            partCount is <= 0 or > 240 ||
            partCount != expectedParts)
        {
            throw new InvalidDataException("Multipart shape did not match the archive.");
        }

        List<CompletedPart> completed = new(partCount);
        long uploaded = 0;
        for (int partNumber = 1; partNumber <= partCount; partNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long offset = (long)(partNumber - 1) * partSize;
            int length = checked((int)Math.Min(partSize, totalBytes - offset));
            string partSha = await HashSegmentAsync(
                path,
                offset,
                length,
                null,
                cancellationToken).ConfigureAwait(false);
            PartUrlResponse partGrant = await SendApiAsync<PartUrlRequest, PartUrlResponse>(
                HttpMethod.Post,
                ApiUri(grant.Id, $"parts/{partNumber}"),
                new PartUrlRequest(length, partSha),
                grant.AuthorizationToken,
                cancellationToken).ConfigureAwait(false);
            Uri uri = TrustedStorageUri(partGrant.Url);
            IReadOnlyDictionary<string, string> headers = ValidateHeaders(
                partGrant.RequiredHeaders,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-amz-checksum-sha256"] = ChecksumHeader(partSha),
                });

            await using FileStream file = OpenArchive(path);
            file.Position = offset;
            using LimitedReadStream segment = new(file, length, ownsInner: false);
            using StreamContent content = new(segment);
            content.Headers.ContentLength = length;
            using HttpRequestMessage request = new(HttpMethod.Put, uri) { Content = content };
            request.Headers.TryAddWithoutValidation(
                "x-amz-checksum-sha256",
                headers["x-amz-checksum-sha256"]);
            progress?.Report(new(
                DiagnosticUploadPhase.Uploading,
                uploaded,
                totalBytes,
                partNumber,
                partCount));
            string? etag = await SendStorageAsync(request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(etag) || etag.Length > 128)
                throw new InvalidDataException("Multipart response ETag was invalid.");
            completed.Add(new CompletedPart(partNumber, etag));
            uploaded += length;
            progress?.Report(new(
                DiagnosticUploadPhase.Uploading,
                uploaded,
                totalBytes,
                partNumber,
                partCount));
        }
        return completed;
    }

    private async Task CompleteAsync(
        CreateUploadResponse grant,
        IReadOnlyList<CompletedPart> parts,
        CancellationToken cancellationToken)
    {
        CompleteUploadResponse response = await SendApiAsync<CompleteUploadRequest, CompleteUploadResponse>(
            HttpMethod.Post,
            ApiUri(grant.Id, "complete"),
            new CompleteUploadRequest(parts),
            grant.AuthorizationToken,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(response.Status, "Verifying", StringComparison.Ordinal))
            throw new InvalidDataException("Diagnostic upload completion state was invalid.");
    }

    private async Task<TResponse> SendApiAsync<TRequest, TResponse>(
        HttpMethod method,
        Uri uri,
        TRequest body,
        string? bearer,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, uri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("LilacMacro-Diagnostics/1.0");
        if (bearer is not null)
        {
            if (bearer.Length is < 20 or > 2048)
                throw new InvalidDataException("Diagnostic upload authorization was invalid.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using HttpResponseMessage response = await _api.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri != uri || IsRedirect(response.StatusCode))
            throw new HttpRequestException("Diagnostic service attempted an untrusted redirect.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Diagnostic service rejected the request.");
        if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Diagnostic service response type was invalid.");
        }
        return await ReadBoundedJsonAsync<TResponse>(response.Content, timeout.Token)
            .ConfigureAwait(false);
    }

    private async Task<string?> SendStorageAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri expected = request.RequestUri
            ?? throw new InvalidDataException("Diagnostic storage URI was missing.");
        using HttpResponseMessage response = await _storage.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri != expected || IsRedirect(response.StatusCode))
            throw new HttpRequestException("Diagnostic storage attempted an untrusted redirect.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Diagnostic archive transfer failed.");
        return response.Headers.ETag?.Tag;
    }

    private static async Task<T> ReadBoundedJsonAsync<T>(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > DiagnosticUploadPolicy.MaximumResponseBytes)
        {
            throw new InvalidDataException("Diagnostic service response was too large.");
        }
        await using Stream source = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using MemoryStream destination = new();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > DiagnosticUploadPolicy.MaximumResponseBytes)
                throw new InvalidDataException("Diagnostic service response was too large.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
        destination.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(destination, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Diagnostic service response was empty.");
    }

    private static async Task<string> HashSegmentAsync(
        string path,
        long offset,
        long length,
        Action<long>? progress,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenArchive(path);
        stream.Position = offset;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        long remaining = length;
        long completed = 0;
        while (remaining > 0)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Diagnostic archive changed while hashing.");
            hash.AppendData(buffer, 0, read);
            remaining -= read;
            completed += read;
            progress?.Invoke(completed);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static FileStream OpenArchive(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        1024 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void ValidateGrant(CreateUploadResponse grant)
    {
        if (grant.Id == Guid.Empty ||
            grant.AuthorizationToken.Length is < 20 or > 2048 ||
            !string.Equals(grant.Status, "Uploading", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Diagnostic upload grant was invalid.");
        }
        _ = ParseTimestamp(grant.ExpiresAt);
    }

    private static Uri TrustedStorageUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !DiagnosticUploadPolicy.IsTrustedStorageUri(uri))
        {
            throw new InvalidDataException("Diagnostic storage destination was untrusted.");
        }
        return uri;
    }

    private static IReadOnlyDictionary<string, string> ValidateHeaders(
        Dictionary<string, string>? actual,
        IReadOnlyDictionary<string, string> expected)
    {
        if (actual is null || actual.Count != expected.Count)
            throw new InvalidDataException("Diagnostic storage headers were invalid.");
        Dictionary<string, string> normalized = new(actual, StringComparer.OrdinalIgnoreCase);
        if (normalized.Count != actual.Count ||
            expected.Any(item => !normalized.TryGetValue(item.Key, out string? value) ||
                                 !string.Equals(value, item.Value, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Diagnostic storage headers were invalid.");
        }
        return normalized;
    }

    private static Uri ApiUri(Guid id, string suffix) => new(
        $"{DiagnosticUploadPolicy.CreateEndpoint.AbsoluteUri}/{id:D}/{suffix}");

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : throw new InvalidDataException("Diagnostic upload timestamp was invalid.");

    private static string ChecksumHeader(string sha256) =>
        Convert.ToBase64String(Convert.FromHexString(sha256));

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
