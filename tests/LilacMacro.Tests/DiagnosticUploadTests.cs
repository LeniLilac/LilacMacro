using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.Tests;

public sealed class DiagnosticUploadTests
{
    [Fact]
    public async Task DiagnosticUploadConsentDefaultsOffAndPersistsExplicitOptIn()
    {
        string root = NewTemporaryDirectory();
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.False(first.EnableDiagnosticUploads);

            first.SetDiagnosticUploadConsent(true);
            await first.FlushAsync();

            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.True(restored.EnableDiagnosticUploads);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PolicyRejectsUntrustedDestinationsAndLargeUploadsWithoutGrant()
    {
        Assert.True(DiagnosticUploadPolicy.IsTrustedStorageUri(new Uri(
            "https://s3.us-west-004.backblazeb2.com/bucket/diagnostics/2026/a.zip?X-Amz-Signature=test")));
        Assert.False(DiagnosticUploadPolicy.IsTrustedStorageUri(new Uri(
            "https://s3.us-west-004.backblazeb2.com.evil.example/bucket/diagnostics/a.zip?x=1")));
        Assert.False(DiagnosticUploadPolicy.IsTrustedStorageUri(new Uri(
            "https://s3.us-west-004.backblazeb2.com/bucket/other/a.zip?x=1")));
        Assert.Throws<InvalidDataException>(() => DiagnosticUploadPolicy.RequireLargeGrant(
            DiagnosticUploadPolicy.RoutineLimitBytes + 1,
            null));
        DiagnosticUploadPolicy.RequireLargeGrant(
            DiagnosticUploadPolicy.RoutineLimitBytes + 1,
            new string('a', 40));
    }

    [Fact]
    public async Task InstallationIdentityIsStableAndCorruptionDoesNotRotateIt()
    {
        string root = NewTemporaryDirectory();
        try
        {
            DiagnosticInstallationStore store = new(root);
            Guid first = await store.GetOrCreateAsync();
            Guid second = await store.GetOrCreateAsync();
            Assert.NotEqual(Guid.Empty, first);
            Assert.Equal(first, second);

            string path = Path.Combine(root, "services", "diagnostic-installation.json");
            await File.WriteAllTextAsync(path, "{\"schema_version\":1,\"install_id\":\"bad\"}");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.GetOrCreateAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SingleUploadBindsArchiveChecksumAndCompletes()
    {
        byte[] archive = Encoding.UTF8.GetBytes("small diagnostic archive");
        string root = NewTemporaryDirectory();
        string path = Path.Combine(root, "deep-debug-test.zip");
        await File.WriteAllBytesAsync(path, archive);
        string sha = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        string checksum = Convert.ToBase64String(SHA256.HashData(archive));
        Guid uploadId = Guid.NewGuid();
        int apiCalls = 0;
        try
        {
            RecordingHandler apiHandler = new(async request =>
            {
                apiCalls++;
                if (apiCalls == 1)
                {
                    using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    Assert.Equal("deep-debug-test.zip", body.RootElement.GetProperty("fileName").GetString());
                    Assert.Equal(sha, body.RootElement.GetProperty("sha256").GetString());
                    Assert.True(body.RootElement.GetProperty("explicitConsent").GetBoolean());
                    return JsonResponse(HttpStatusCode.Created, new
                    {
                        id = uploadId,
                        authorizationToken = new string('t', 48),
                        status = "Uploading",
                        expiresAt = "2026-08-17T00:00:00Z",
                        acceptanceDeadline = (string?)null,
                        upload = new
                        {
                            kind = "single",
                            url = StorageUri(uploadId, 0).AbsoluteUri,
                            requiredHeaders = new Dictionary<string, string>
                            {
                                ["content-type"] = "application/zip",
                                ["x-amz-checksum-sha256"] = checksum,
                                ["x-amz-server-side-encryption"] = "AES256",
                            },
                        },
                    }, request);
                }

                Assert.EndsWith($"/{uploadId:D}/complete", request.RequestUri!.AbsolutePath);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                using JsonDocument complete = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Empty(complete.RootElement.GetProperty("parts").EnumerateArray());
                return JsonResponse(HttpStatusCode.Accepted, new { status = "Verifying" }, request);
            });
            RecordingHandler storageHandler = new(async request =>
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal(checksum, request.Headers.GetValues("x-amz-checksum-sha256").Single());
                Assert.Equal("AES256", request.Headers.GetValues("x-amz-server-side-encryption").Single());
                Assert.Equal(archive.Length, request.Content!.Headers.ContentLength);
                Assert.Equal(archive, await request.Content.ReadAsByteArrayAsync());
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            });
            using DiagnosticUploadTransport transport = CreateTransport(apiHandler, storageHandler);

            DiagnosticUploadResult result = await transport.UploadAsync(
                path,
                DiagnosticArchiveKind.DeepDebug,
                "1.2.3",
                Guid.NewGuid(),
                null);

            Assert.Equal(uploadId, result.UploadId);
            Assert.Equal("Verifying", result.Status);
            Assert.Equal(2, apiCalls);
            Assert.Single(storageHandler.Requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadRejectsUnexpectedCompletionState()
    {
        byte[] archive = Encoding.UTF8.GetBytes("diagnostic archive");
        string root = NewTemporaryDirectory();
        string path = Path.Combine(root, "deep-debug-test.zip");
        await File.WriteAllBytesAsync(path, archive);
        Guid uploadId = Guid.NewGuid();
        int calls = 0;
        try
        {
            RecordingHandler apiHandler = new(request =>
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? JsonResponse(HttpStatusCode.Created, new
                    {
                        id = uploadId,
                        authorizationToken = new string('t', 48),
                        status = "Uploading",
                        expiresAt = "2026-08-17T00:00:00Z",
                        acceptanceDeadline = (string?)null,
                        upload = new
                        {
                            kind = "single",
                            url = StorageUri(uploadId, 0).AbsoluteUri,
                            requiredHeaders = RequiredSingleHeaders(archive),
                        },
                    }, request)
                    : JsonResponse(HttpStatusCode.OK, new { status = "Accepted" }, request));
            });
            RecordingHandler storageHandler = new(request => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request }));
            using DiagnosticUploadTransport transport = CreateTransport(apiHandler, storageHandler);

            InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
                transport.UploadAsync(
                    path,
                    DiagnosticArchiveKind.DeepDebug,
                    "1.2.3",
                    Guid.NewGuid(),
                    null));

            Assert.Contains("completion state", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MultipartUploadBindsEveryPartAndCompletesInOrder()
    {
        byte[] archive = Encoding.ASCII.GetBytes("abcdefg");
        string root = NewTemporaryDirectory();
        string path = Path.Combine(root, "live-debug-test.zip");
        await File.WriteAllBytesAsync(path, archive);
        Guid uploadId = Guid.NewGuid();
        Dictionary<int, byte[]> expectedParts = new()
        {
            [1] = archive[..4],
            [2] = archive[4..],
        };
        try
        {
            RecordingHandler apiHandler = new(async request =>
            {
                string route = request.RequestUri!.AbsolutePath;
                if (route.EndsWith("/uploads", StringComparison.Ordinal))
                {
                    return JsonResponse(HttpStatusCode.Created, new
                    {
                        id = uploadId,
                        authorizationToken = new string('m', 48),
                        status = "Uploading",
                        expiresAt = "2026-08-17T00:00:00Z",
                        acceptanceDeadline = (string?)null,
                        upload = new
                        {
                            kind = "multipart",
                            uploadId = "provider-upload",
                            partSizeBytes = 4,
                            partCount = 2,
                        },
                    }, request);
                }
                if (route.Contains("/parts/", StringComparison.Ordinal))
                {
                    int number = int.Parse(route[(route.LastIndexOf('/') + 1)..]);
                    string checksum = Convert.ToBase64String(SHA256.HashData(expectedParts[number]));
                    using JsonDocument grant = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    Assert.Equal(expectedParts[number].Length, grant.RootElement.GetProperty("sizeBytes").GetInt32());
                    return JsonResponse(HttpStatusCode.OK, new
                    {
                        url = StorageUri(uploadId, number).AbsoluteUri,
                        requiredHeaders = new Dictionary<string, string>
                        {
                            ["x-amz-checksum-sha256"] = checksum,
                        },
                    }, request);
                }

                using JsonDocument complete = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                int[] partNumbers = complete.RootElement.GetProperty("parts")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("partNumber").GetInt32())
                    .ToArray();
                Assert.Equal([1, 2], partNumbers);
                return JsonResponse(HttpStatusCode.Accepted, new { status = "Verifying" }, request);
            });
            RecordingHandler storageHandler = new(async request =>
            {
                int number = int.Parse(request.RequestUri!.AbsolutePath.Split('-').Last()[..1]);
                byte[] body = await request.Content!.ReadAsByteArrayAsync();
                Assert.Equal(expectedParts[number], body);
                HttpResponseMessage response = new(HttpStatusCode.OK) { RequestMessage = request };
                response.Headers.ETag = new EntityTagHeaderValue($"\"0123456789abcde{number}\"");
                return response;
            });
            using DiagnosticUploadTransport transport = CreateTransport(apiHandler, storageHandler);

            DiagnosticUploadResult result = await transport.UploadAsync(
                path,
                DiagnosticArchiveKind.LiveDebug,
                "1.2.3",
                Guid.NewGuid(),
                null);

            Assert.Equal(uploadId, result.UploadId);
            Assert.Equal(2, storageHandler.Requests.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DiagnosticUploadTransport CreateTransport(
        HttpMessageHandler api,
        HttpMessageHandler storage) => new(
        new HttpClient(api) { Timeout = Timeout.InfiniteTimeSpan },
        new HttpClient(storage) { Timeout = Timeout.InfiniteTimeSpan },
        ownsClients: true);

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode status,
        object body,
        HttpRequestMessage request) => new(status)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

    private static Uri StorageUri(Guid id, int part) => new(
        $"https://s3.us-west-004.backblazeb2.com/test-bucket/diagnostics/2026/{id:D}-part-{part}.zip?X-Amz-Signature=test");

    private static Dictionary<string, string> RequiredSingleHeaders(byte[] content) => new()
    {
        ["content-type"] = "application/zip",
        ["x-amz-checksum-sha256"] = Convert.ToBase64String(SHA256.HashData(content)),
        ["x-amz-server-side-encryption"] = "AES256",
    };

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lilac-diagnostic-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await handler(request);
        }
    }
}
