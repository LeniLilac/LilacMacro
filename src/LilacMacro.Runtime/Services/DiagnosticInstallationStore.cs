using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilacMacro.Runtime.Services;

public sealed class DiagnosticInstallationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _path;

    public string ConfigurationRoot { get; }

    public DiagnosticInstallationStore(string configurationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRoot);
        ConfigurationRoot = Path.GetFullPath(configurationRoot);
        _path = Path.Combine(ConfigurationRoot, "services", "diagnostic-installation.json");
    }

    public async Task<Guid> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Diagnostic identity path has no parent.");
        Directory.CreateDirectory(directory);
        string lockPath = _path + ".write.lock";
        await using FileStream writeLock = await AcquireLockAsync(lockPath, cancellationToken)
            .ConfigureAwait(false);
        if (File.Exists(_path)) return await ReadAsync(cancellationToken).ConfigureAwait(false);

        Guid installId = Guid.NewGuid();
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new InstallationDocument(1, installId),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: false);
            return installId;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<Guid> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            InstallationDocument? document = await JsonSerializer.DeserializeAsync<InstallationDocument>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (document is not { SchemaVersion: 1 } || document.InstallId == Guid.Empty)
                throw new InvalidDataException("Diagnostic installation identity is invalid.");
            return document.InstallId;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Diagnostic installation identity is invalid.", exception);
        }
    }

    private static async Task<FileStream> AcquireLockAsync(
        string path,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed record InstallationDocument(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("install_id")] Guid InstallId);
}
