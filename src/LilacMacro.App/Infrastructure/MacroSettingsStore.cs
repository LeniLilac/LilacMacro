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
    private readonly string _privacyPath;

    internal bool Exists => File.Exists(_settingsPath);

    public MacroSettingsStore()
        : this(MacroInstanceContext.Current.ConfigurationRoot)
    {
    }

    internal MacroSettingsStore(string appDataRoot)
    {
        _settingsPath = Path.Combine(appDataRoot, "macro-settings.json");
        _privacyPath = Path.Combine(appDataRoot, "privacy-choices.json");
    }

    public async Task<MacroSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        MacroSettings loaded = new();
        try
        {
            if (File.Exists(_settingsPath))
            {
                await using FileStream stream = File.OpenRead(_settingsPath);
                MacroSettings? settings = await JsonSerializer.DeserializeAsync<MacroSettings>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                loaded = settings?.SchemaVersion switch
                {
                    MacroSettings.CurrentSchemaVersion => settings,
                    1 or 2 => MigrateLegacySettings(settings),
                    3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 =>
                        MigratePreEventSettings(settings),
                    _ => new MacroSettings(),
                };
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            loaded = new MacroSettings();
        }
        PersistedPrivacyChoices? privacy = await LoadPrivacyChoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        return privacy is null ? loaded : ApplyPrivacy(loaded, privacy);
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
            NotifyOnRunStart = false,
            NotifyOnRunStop = false,
            NotifyOnTaskChange = false,
            NotifyOnVictory = false,
            NotifyOnDefeat = false,
            NotifyOnRecovery = false,
        };
    }

    private static MacroSettings MigratePreEventSettings(MacroSettings settings) => settings with
    {
        SchemaVersion = MacroSettings.CurrentSchemaVersion,
        NotifyOnRunStart = false,
        NotifyOnRunStop = false,
        NotifyOnTaskChange = false,
        NotifyOnVictory = false,
        NotifyOnDefeat = false,
        NotifyOnRecovery = false,
    };

    public async Task SaveAsync(MacroSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Macro settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        await using FileStream writeLock = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
        PersistedPrivacyChoices? privacy = await LoadPrivacyChoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (privacy is not null) settings = ApplyPrivacy(settings, privacy);
        await WriteAtomicAsync(_settingsPath, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersistedPrivacyChoices> SavePrivacyChoicesAsync(
        bool? onlineFeaturesEnabled,
        bool? telemetryEnabled,
        bool? automaticErrorReportsEnabled,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_privacyPath)
            ?? throw new InvalidOperationException("Privacy settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        await using FileStream writeLock = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
        PersistedPrivacyChoices? current = await LoadPrivacyChoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        PersistedPrivacyChoices desired = new()
        {
            Generation = checked((current?.Generation ?? 0) + 1),
            NoticeVersion = PrivacyChoicesPolicy.CurrentNoticeVersion,
            OnlineFeaturesEnabled = onlineFeaturesEnabled ?? current?.OnlineFeaturesEnabled ?? false,
            TelemetryEnabled = telemetryEnabled ?? current?.TelemetryEnabled ?? false,
            AutomaticErrorReportsEnabled = automaticErrorReportsEnabled
                ?? current?.AutomaticErrorReportsEnabled ?? false,
        };
        await WriteAtomicAsync(_privacyPath, desired, cancellationToken).ConfigureAwait(false);
        return desired;
    }

    public async Task<PersistedPrivacyChoices?> LoadPrivacyChoicesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_privacyPath)) return null;
        try
        {
            await using FileStream stream = new(
                _privacyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                8192,
                useAsync: true);
            PersistedPrivacyChoices? choices = await JsonSerializer.DeserializeAsync<PersistedPrivacyChoices>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return choices is
            {
                SchemaVersion: PersistedPrivacyChoices.CurrentSchemaVersion,
                Generation: >= 1,
                NoticeVersion: >= 0,
            } ? choices : DisabledPrivacyChoices();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return DisabledPrivacyChoices();
        }
    }

    public PersistedPrivacyChoices? LoadPrivacyChoices()
    {
        if (!File.Exists(_privacyPath)) return null;
        try
        {
            using FileStream stream = new(
                _privacyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            PersistedPrivacyChoices? choices = JsonSerializer.Deserialize<PersistedPrivacyChoices>(
                stream,
                JsonOptions);
            return choices is
            {
                SchemaVersion: PersistedPrivacyChoices.CurrentSchemaVersion,
                Generation: >= 1,
                NoticeVersion: >= 0,
            } ? choices : DisabledPrivacyChoices();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return DisabledPrivacyChoices();
        }
    }

    private static MacroSettings ApplyPrivacy(MacroSettings settings, PersistedPrivacyChoices privacy) =>
        settings with
        {
            PrivacyChoicesVersion = privacy.NoticeVersion,
            OnlineFeaturesEnabled = privacy.OnlineFeaturesEnabled,
            TelemetryEnabled = privacy.TelemetryEnabled,
            AutomaticErrorReportsEnabled = privacy.AutomaticErrorReportsEnabled,
        };

    private static PersistedPrivacyChoices DisabledPrivacyChoices() => new()
    {
        Generation = 1,
        NoticeVersion = 0,
        OnlineFeaturesEnabled = false,
        TelemetryEnabled = false,
        AutomaticErrorReportsEnabled = false,
    };

    private static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
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
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
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
