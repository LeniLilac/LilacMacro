namespace LilacMacro.Core.Services;

public static class ControlFeatureIds
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "mode.story",
        "mode.raid",
        "mode.challenge",
        "mode.expedition",
        "mode.event",
        "task.calendar-claim",
        "task.gold-shop",
        "task.raid-shop",
        "task.expedition-shop",
        "task.code-redeem",
        "task.gold-mine-refuel",
        "task.resource-drill-refuel",
        "feature.route-optimizer",
        "feature.team-swap",
        "feature.settings-normalizer",
    };
}

public static class ControlScheduleKeys
{
    public const string GoldShopReset = "gold-shop-reset";
    public const string RaidShopReset = "raid-shop-reset";
    public const string ExpeditionShopReset = "expedition-shop-reset";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        GoldShopReset,
        RaidShopReset,
        ExpeditionShopReset,
    };
}

public sealed record ControlGameAvailability(
    bool Available,
    bool OperatorAvailable,
    bool? ObservedPublic,
    DateTimeOffset? ObservedAt,
    string? Message);

public sealed record ControlRedeemCode(string Code, DateTimeOffset? ExpiresAt);

public sealed record ControlSchedule(
    string Key,
    DateTimeOffset NextAt,
    int CadenceSeconds);

public sealed record ControlDisablement(
    string Feature,
    string Reason,
    DateTimeOffset? ExpiresAt);

public sealed record ControlRelease(
    Version Version,
    Uri PageUrl,
    Uri InstallerUrl,
    DateTimeOffset PublishedAt);

public sealed record ControlPayload(
    long Revision,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExpiresAt,
    ControlGameAvailability Game,
    IReadOnlyList<ControlRedeemCode> Codes,
    IReadOnlyList<ControlSchedule> Schedules,
    IReadOnlyList<ControlDisablement> Disablements,
    ControlRelease? Release);

public sealed record SignedControlSnapshot(
    string KeyId,
    string Algorithm,
    ControlPayload Payload,
    string Signature);
