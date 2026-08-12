namespace LilacMacro.Core.LocalSession;

public sealed record RunnerPackageRule(string PackageFamilyName, bool RemoveWhenPresent);

public sealed record RunnerRegistryRule(
    string RelativeKey,
    string ValueName,
    string ValueKind,
    string EncodedValue,
    bool DeleteWhenPresent = false);

public sealed record RunnerProfileReceipt
{
    public int SchemaVersion { get; init; } = 1;
    public string PolicyVersion { get; init; } = string.Empty;
    public string RunnerSid { get; init; } = string.Empty;
    public IReadOnlyList<string> RemovedPackages { get; init; } = [];
    public IReadOnlyList<string> AppliedRegistryRules { get; init; } = [];
    public DateTimeOffset AppliedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RunnerProfileFailure
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string FailureCode { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RunnerProfilePolicy
{
    public const string CurrentVersion = "1.6.0";

    public string Version { get; init; } = CurrentVersion;
    public IReadOnlyList<RunnerPackageRule> PackageRules { get; init; } = DefaultPackageRules;
    public IReadOnlyList<RunnerRegistryRule> RegistryRules { get; init; } = DefaultRegistryRules;

    public static IReadOnlyList<RunnerPackageRule> DefaultPackageRules { get; } =
    [
        new("Clipchamp.Clipchamp", false),
        new("Microsoft.549981C3F5F10", false),
        new("Microsoft.MicrosoftOfficeHub", false),
        new("MSTeams", false),
        new("MicrosoftTeams", false),
    ];

    public static IReadOnlyList<RunnerRegistryRule> DefaultRegistryRules { get; } =
    [
        Dword(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "ContentDeliveryAllowed", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "OemPreInstalledAppsEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "PreInstalledAppsEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "PreInstalledAppsEverEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338387Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338393Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons", 1),
        String(@"Control Panel\Desktop", "Wallpaper", string.Empty),
        String(@"Control Panel\Desktop", "WallpaperStyle", "0"),
        String(@"Control Panel\Desktop", "TileWallpaper", "0"),
        String(@"Control Panel\Colors", "Background", "0 0 0"),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
        Dword(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0),
        Dword(@"Software\Policies\Microsoft\Windows\OOBE", "DisablePrivacyExperience", 1),
        Dword(@"Software\Policies\Microsoft\Edge", "HideFirstRunExperience", 1),
        Delete(@"Software\Microsoft\Windows\CurrentVersion\Run", "OneDrive"),
        Delete(@"Software\Microsoft\Windows\CurrentVersion\Run", "MSTeams"),
        Delete(@"Software\Microsoft\Windows\CurrentVersion\Run", "com.squirrel.Teams.Teams"),
    ];

    private static RunnerRegistryRule Dword(string key, string name, int value) =>
        new(key, name, "DWord", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static RunnerRegistryRule String(string key, string name, string value) =>
        new(key, name, "String", value);

    private static RunnerRegistryRule Delete(string key, string name) =>
        new(key, name, "String", string.Empty, DeleteWhenPresent: true);
}
