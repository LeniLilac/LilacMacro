using System.Text.RegularExpressions;

namespace LilacMacro.Core.Services;

public enum DiagnosticArchiveKind
{
    DeepDebug,
    RuntimeLog,
    InstallerLog,
    LiveDebug,
}

public enum DiagnosticUploadPhase
{
    Preparing,
    Hashing,
    Uploading,
    Finalizing,
    Complete,
}

public sealed record DiagnosticUploadProgress(
    DiagnosticUploadPhase Phase,
    long BytesCompleted,
    long TotalBytes,
    int? PartNumber = null,
    int? PartCount = null);

public sealed record DiagnosticUploadResult(
    Guid UploadId,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptanceDeadline);

public static partial class DiagnosticUploadPolicy
{
    public const long OneGiB = 1024L * 1024 * 1024;
    public const long RoutineLimitBytes = 3 * OneGiB;
    public const long AbsoluteLimitBytes = 30 * OneGiB;
    public const int MaximumFileNameLength = 160;
    public const int MaximumResponseBytes = 64 * 1024;
    public static readonly Uri CreateEndpoint = new(
        "https://macro.expeditions.gg/v1/diagnostics/uploads");

    public static string ValidateArchive(string path, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sizeBytes is <= 0 or > AbsoluteLimitBytes)
            throw new InvalidDataException("Diagnostic archive size is outside the supported range.");
        string fileName = Path.GetFileName(path);
        if (fileName.Length is < 1 or > MaximumFileNameLength ||
            !ArchiveFileNamePattern().IsMatch(fileName))
        {
            throw new InvalidDataException("Diagnostic archive name is invalid.");
        }
        return fileName;
    }

    public static string KindValue(DiagnosticArchiveKind kind) => kind switch
    {
        DiagnosticArchiveKind.DeepDebug => "deep-debug",
        DiagnosticArchiveKind.RuntimeLog => "runtime-log",
        DiagnosticArchiveKind.InstallerLog => "installer-log",
        DiagnosticArchiveKind.LiveDebug => "live-debug",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static void RequireLargeGrant(long sizeBytes, string? grant)
    {
        if (sizeBytes > RoutineLimitBytes && string.IsNullOrWhiteSpace(grant))
            throw new InvalidDataException(
                "Archives over 3 GiB require a short-lived administrator grant.");
    }

    public static bool IsTrustedStorageUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        StorageHostPattern().IsMatch(uri.IdnHost) &&
        uri.AbsolutePath.Contains("/diagnostics/", StringComparison.Ordinal) &&
        !string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._ -]*\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveFileNamePattern();

    [GeneratedRegex(@"^s3\.[a-z0-9-]+\.backblazeb2\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StorageHostPattern();
}
