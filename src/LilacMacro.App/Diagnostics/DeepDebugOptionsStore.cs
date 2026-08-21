using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugOptionsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public DeepDebugOptionsStore(string appDataRoot) =>
        _path = Path.Combine(appDataRoot, "deep-debug-settings.json");

    public DeepDebugOptions Load()
    {
        try
        {
            if (!File.Exists(_path)) return new DeepDebugOptions();
            DeepDebugLocalSettings? loaded = JsonSerializer.Deserialize<DeepDebugLocalSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return new DeepDebugOptions { Enabled = loaded?.Enabled ?? true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new DeepDebugOptions();
        }
    }

    public async Task SaveAsync(DeepDebugOptions options)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(new DeepDebugLocalSettings
                {
                    Enabled = options.Enabled,
                }, JsonOptions),
                CancellationToken.None);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record DeepDebugLocalSettings
    {
        public int SchemaVersion { get; init; } = 2;
        public bool Enabled { get; init; } = true;
    }
}
