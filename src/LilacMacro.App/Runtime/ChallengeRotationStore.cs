using System.Text.Json;
using System.Text.Json.Serialization;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class ChallengeRotationStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly string _path;

    public ChallengeRotationStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilacMacro",
        "challenge-rotation.json");

    public async Task<ChallengeRotationProgress> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return ChallengeRotationProgress.Empty;
        try
        {
            await using FileStream stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<ChallengeRotationProgress>(stream, JsonOptions, cancellationToken)
                ?? ChallengeRotationProgress.Empty;
        }
        catch (IOException) { return ChallengeRotationProgress.Empty; }
        catch (JsonException) { return ChallengeRotationProgress.Empty; }
    }

    public async Task SaveAsync(ChallengeRotationProgress progress, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Challenge rotation path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                await JsonSerializer.SerializeAsync(stream, progress, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
