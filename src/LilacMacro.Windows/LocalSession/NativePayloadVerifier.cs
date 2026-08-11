using System.Security.Cryptography;
using System.Text.Json.Serialization;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

internal sealed record NativePayloadManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    [JsonPropertyName("source_commit")]
    public string SourceCommit { get; init; } = string.Empty;
    public IReadOnlyList<NativePayloadFile> Files { get; init; } = [];
}

public sealed record NativePayloadVerification(
    bool IsValid,
    string Version,
    IReadOnlyList<NativePayloadFile> Files,
    IReadOnlyList<string> Errors);

public sealed class NativePayloadVerifier(LocalSessionPaths paths)
{
    private static readonly string[] RequiredRelativePaths =
    [
        "x64/TermWrap.dll",
        "x64/Zydis.dll",
    ];

    public async Task<NativePayloadVerification> VerifyAsync(CancellationToken cancellationToken = default)
    {
        NativePayloadManifest? manifest;
        try
        {
            manifest = await AtomicJsonFile.ReadAsync<NativePayloadManifest>(paths.PayloadManifestPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return Invalid($"Native payload manifest could not be read: {exception.Message}");
        }

        if (manifest is null || manifest.SchemaVersion != 1 || !string.Equals(manifest.Version, "0.6", StringComparison.Ordinal))
            return Invalid("Native payload manifest is missing or unsupported.");

        List<string> errors = [];
        HashSet<string> manifestPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (NativePayloadFile file in manifest.Files)
        {
            string normalized = file.RelativePath.Replace('\\', '/');
            if (!manifestPaths.Add(normalized))
            {
                errors.Add($"Native payload contains a duplicate path: {file.RelativePath}");
                continue;
            }
            string relative = normalized.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(paths.NativePayloadRoot, relative));
            if (!fullPath.StartsWith(Path.GetFullPath(paths.NativePayloadRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Unsafe native payload path: {file.RelativePath}");
                continue;
            }
            if (!File.Exists(fullPath))
            {
                errors.Add($"Native payload is missing: {file.RelativePath}");
                continue;
            }
            FileInfo info = new(fullPath);
            if (info.Length != file.Size)
            {
                errors.Add($"Native payload size mismatch: {file.RelativePath}");
                continue;
            }
            await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(file.Sha256)))
                errors.Add($"Native payload hash mismatch: {file.RelativePath}");
        }
        foreach (string requiredPath in RequiredRelativePaths)
        {
            if (!manifestPaths.Contains(requiredPath))
                errors.Add($"Native payload manifest is missing required file: {requiredPath}");
        }
        return new(errors.Count == 0, manifest.Version, manifest.Files, errors);
    }

    private static NativePayloadVerification Invalid(string error) => new(false, string.Empty, [], [error]);
}
