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
            DeepDebugOptions? loaded = JsonSerializer.Deserialize<DeepDebugOptions>(File.ReadAllText(_path), JsonOptions);
            return Normalize(loaded ?? new DeepDebugOptions());
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
                JsonSerializer.Serialize(Normalize(options), JsonOptions),
                CancellationToken.None);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static DeepDebugOptions Normalize(DeepDebugOptions options) => options with
    {
        FrameRetentionMinutes = DeepDebugOptions.NormalizeFrameRetention(options.FrameRetentionMinutes),
        CaptureIntervalMilliseconds = DeepDebugOptions.NormalizeCaptureInterval(
            options.CaptureIntervalMilliseconds),
    };
}
