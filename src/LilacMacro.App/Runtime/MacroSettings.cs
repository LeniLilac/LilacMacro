using LilacMacro.App.Theming;

namespace LilacMacro.App.Runtime;

internal enum PrivacyChoiceKind
{
    OnlineFeatures,
    Telemetry,
    AutomaticErrorReports,
}

internal sealed record MacroSettings
{
    public const int CurrentSchemaVersion = 13;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Dictionary<string, int?> KeyBindings { get; init; } = [];

    public List<PlanSettingsSnapshot> Plans { get; init; } = [];

    public int SelectedPlanIndex { get; init; }

    public string EncryptedPrivateServerLink { get; init; } = string.Empty;

    public string EncryptedDiscordWebhook { get; init; } = string.Empty;

    public string DiscordUserId { get; init; } = string.Empty;

    public bool NotifyOnTerminalFailure { get; init; } = true;

    public bool NotifyOnRunStart { get; init; } = true;

    public bool NotifyOnRunStop { get; init; } = true;

    public bool NotifyOnTaskChange { get; init; } = true;

    public bool NotifyOnVictory { get; init; } = true;

    public bool NotifyOnDefeat { get; init; } = true;

    public bool NotifyOnRecovery { get; init; } = true;

    public int PrivacyChoicesVersion { get; init; }

    public bool OnlineFeaturesEnabled { get; init; } = true;

    public bool TelemetryEnabled { get; init; } = true;

    public bool AutomaticErrorReportsEnabled { get; init; }

    public bool CheckForUpdatesOnStartup { get; init; } = true;

    public bool IncludePrereleaseUpdates { get; init; }

    public MacroLayoutProfile LayoutProfile { get; init; } = MacroLayoutProfile.Full1920x1080;

    public MacroMinimizeBehavior MinimizeBehavior { get; init; } = MacroMinimizeBehavior.WhileRunning;

    public AppTheme ThemeMode { get; init; } = AppTheme.Light;

    public AppColorTheme ColorTheme { get; init; } = AppColorTheme.Lilac;

    public Dictionary<string, MacroLayoutProfile> RunnerLayoutProfiles { get; init; } = [];
}

internal sealed record PersistedPrivacyChoices
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public long Generation { get; init; }

    public int NoticeVersion { get; init; }

    public bool OnlineFeaturesEnabled { get; init; }

    public bool TelemetryEnabled { get; init; }

    public bool AutomaticErrorReportsEnabled { get; init; }
}
