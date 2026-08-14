using System.Text.Json;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionColorProfileStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.Combine(ResolveRoot(), "expedition-node-colors.json");

    public async Task<ExpeditionNodeColorProfile> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_path)) return new ExpeditionNodeColorProfile();
            await using FileStream stream = File.OpenRead(_path);
            ExpeditionNodeColorProfile? profile = await JsonSerializer.DeserializeAsync<ExpeditionNodeColorProfile>(
                stream, Json, cancellationToken).ConfigureAwait(false);
            return profile?.Version == ExpeditionNodeColorProfile.CurrentVersion
                ? profile
                : new ExpeditionNodeColorProfile();
        }
        catch (JsonException) { return new ExpeditionNodeColorProfile(); }
        catch (IOException) { return new ExpeditionNodeColorProfile(); }
        catch (UnauthorizedAccessException) { return new ExpeditionNodeColorProfile(); }
    }

    public async Task SaveAsync(ExpeditionNodeColorProfile profile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous);
            await JsonSerializer.SerializeAsync(stream, profile, Json, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ResolveRoot() => Environment.GetEnvironmentVariable("LILACMACRO_RUNNER_VISUAL_PROFILES")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro", "visual-profiles", "current-instance");
}
