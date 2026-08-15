using System.Text.Json.Serialization;

namespace LilacMacro.Runtime.Services;

internal sealed record CreateUploadRequest(
    [property: JsonPropertyName("installId")] Guid InstallId,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("explicitConsent")] bool ExplicitConsent,
    [property: JsonPropertyName("appVersion")] string AppVersion,
    [property: JsonPropertyName("largeUploadGrant")] string? LargeUploadGrant);

internal sealed record CreateUploadResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("authorizationToken")] string AuthorizationToken,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt,
    [property: JsonPropertyName("acceptanceDeadline")] string? AcceptanceDeadline,
    [property: JsonPropertyName("upload")] UploadDescriptor Upload);

internal sealed record UploadDescriptor(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("requiredHeaders")] Dictionary<string, string>? RequiredHeaders,
    [property: JsonPropertyName("uploadId")] string? UploadId,
    [property: JsonPropertyName("partSizeBytes")] int? PartSizeBytes,
    [property: JsonPropertyName("partCount")] int? PartCount);

internal sealed record PartUrlRequest(
    [property: JsonPropertyName("sizeBytes")] int SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record PartUrlResponse(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("requiredHeaders")] Dictionary<string, string> RequiredHeaders);

internal sealed record CompletedPart(
    [property: JsonPropertyName("partNumber")] int PartNumber,
    [property: JsonPropertyName("etag")] string Etag);

internal sealed record CompleteUploadRequest(
    [property: JsonPropertyName("parts")] IReadOnlyList<CompletedPart> Parts);

internal sealed record CompleteUploadResponse(
    [property: JsonPropertyName("status")] string Status);
