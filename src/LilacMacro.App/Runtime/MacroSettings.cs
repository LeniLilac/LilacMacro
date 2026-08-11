namespace LilacMacro.App.Runtime;

internal sealed record MacroSettings
{
    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Dictionary<string, int?> KeyBindings { get; init; } = [];

    public List<PlanSettingsSnapshot> Plans { get; init; } = [];

    public int SelectedPlanIndex { get; init; }

    public string EncryptedPrivateServerLink { get; init; } = string.Empty;

    public string EncryptedDiscordWebhook { get; init; } = string.Empty;

    public string DiscordUserId { get; init; } = string.Empty;

    public bool NotifyOnTerminalFailure { get; init; } = true;

    public bool IncludeFailureDetails { get; init; }

    public bool CheckForUpdatesOnStartup { get; init; } = true;

    public bool IncludePrereleaseUpdates { get; init; }

    public MacroLayoutProfile LayoutProfile { get; init; } = MacroLayoutProfile.Full1920x1080;

    public MacroMinimizeBehavior MinimizeBehavior { get; init; } = MacroMinimizeBehavior.WhileRunning;
}
