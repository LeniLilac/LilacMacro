using System.Text.Json;
using LilacMacro.App.Runtime;

namespace LilacMacro.App.Infrastructure;

internal sealed class MacroSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    internal bool Exists => File.Exists(_settingsPath);

    public MacroSettingsStore()
        : this(MacroInstanceContext.Current.ConfigurationRoot)
    {
    }

    internal MacroSettingsStore(string appDataRoot) =>
        _settingsPath = Path.Combine(appDataRoot, "macro-settings.json");

    public async Task<MacroSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath)) return new MacroSettings();
        try
        {
            await using FileStream stream = File.OpenRead(_settingsPath);
            MacroSettings? settings = await JsonSerializer.DeserializeAsync<MacroSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return settings?.SchemaVersion switch
            {
                MacroSettings.CurrentSchemaVersion => settings,
                1 or 2 => MigrateLegacySettings(settings),
                3 or 4 or 5 or 6 or 7 or 8 => settings with { SchemaVersion = MacroSettings.CurrentSchemaVersion },
                _ => new MacroSettings(),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MacroSettings();
        }
    }

    private static MacroSettings MigrateLegacySettings(MacroSettings settings)
    {
        Dictionary<string, int?> keyBindings = new(settings.KeyBindings, StringComparer.OrdinalIgnoreCase);
        string macroToggle = nameof(MacroKeyBindingId.MacroToggle);
        if (keyBindings.TryGetValue(macroToggle, out int? virtualKey) && virtualKey == 0x75)
            keyBindings[macroToggle] = 0x76;
        return settings with
        {
            SchemaVersion = MacroSettings.CurrentSchemaVersion,
            KeyBindings = keyBindings,
        };
    }

    public async Task SaveAsync(MacroSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Macro settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        await using FileStream writeLock = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
        string temporary = _settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                8192,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<FileStream> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        string lockPath = _settingsPath + ".write.lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
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
}
