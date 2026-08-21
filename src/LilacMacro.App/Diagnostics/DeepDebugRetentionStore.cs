using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugRetentionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly string _lockPath;

    public DeepDebugRetentionStore(string diagnosticsRoot)
    {
        _path = Path.Combine(diagnosticsRoot, "deep-debug-retention.json");
        _lockPath = _path + ".write.lock";
    }

    public int Load(int recommendedStorageGiB)
    {
        try
        {
            if (!File.Exists(_path)) return Normalize(recommendedStorageGiB);
            DeepDebugRetentionSettings? loaded = JsonSerializer.Deserialize<DeepDebugRetentionSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return loaded is { SchemaVersion: 2 }
                ? Normalize(loaded.MaximumArchiveStorageGiB)
                : Normalize(recommendedStorageGiB);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Normalize(recommendedStorageGiB);
        }
    }

    public async Task<int> SaveAsync(int maximumArchiveStorageGiB)
    {
        int normalized = Normalize(maximumArchiveStorageGiB);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using FileStream writeLock = await AcquireWriteLockAsync();
        string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(new DeepDebugRetentionSettings
                {
                    MaximumArchiveStorageGiB = normalized,
                }, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
            return normalized;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<FileStream> AcquireWriteLockAsync()
    {
        while (true)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(50);
            }
        }
    }

    private static int Normalize(int value) => Math.Clamp(
        value,
        DeepDebugStoragePolicy.MinimumStorageGiB,
        DeepDebugStoragePolicy.MaximumStorageGiB);

    private sealed record DeepDebugRetentionSettings
    {
        public int SchemaVersion { get; init; } = 2;
        public int MaximumArchiveStorageGiB { get; init; } =
            DeepDebugStoragePolicy.MediumStorageGiB;
    }
}
