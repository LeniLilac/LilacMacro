using LilacMacro.App.Views;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Services;

namespace LilacMacro.App.Runtime;

internal static class MacroControlPolicy
{
    public static bool IsTaskEnabled(
        SignedControlSnapshot? snapshot,
        PlanTaskPrototype task,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        foreach (string feature in FeaturesFor(task))
        {
            if (!ControlOperationalPolicy.IsFeatureEnabled(snapshot, feature, now)) return false;
        }
        return true;
    }

    public static bool IsSettingsNormalizerEnabled(
        SignedControlSnapshot? snapshot,
        DateTimeOffset now) => ControlOperationalPolicy.IsFeatureEnabled(
            snapshot,
            "feature.settings-normalizer",
            now);

    public static bool IsTeamSwapEnabled(
        SignedControlSnapshot? snapshot,
        DateTimeOffset now) => ControlOperationalPolicy.IsFeatureEnabled(
            snapshot,
            "feature.team-swap",
            now);

    public static DateTimeOffset NextUtilityDue(
        SignedControlSnapshot? snapshot,
        PlanTaskPrototype task,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(task);
        DateTimeOffset local = UtilityTaskPolicy.NextDue(task.Route, completedAt, task.Target);
        string? key = task.Route switch
        {
            ShopPurchasePolicy.GoldRoute => ControlScheduleKeys.GoldShopReset,
            ShopPurchasePolicy.RaidRoute => ControlScheduleKeys.RaidShopReset,
            ShopPurchasePolicy.ExpeditionRoute => ControlScheduleKeys.ExpeditionShopReset,
            _ => null,
        };
        if (key is null) return local;
        ControlSchedule? schedule = ControlOperationalPolicy.Schedule(snapshot, key);
        if (schedule is null) return local;
        return ControlOperationalPolicy.NextScheduledOccurrence(
            schedule,
            completedAt);
    }

    private static IReadOnlyList<string> FeaturesFor(PlanTaskPrototype task)
    {
        if (task.Mode == PlanTaskMode.Utilities) return task.Route switch
        {
            UtilityTaskPolicy.CalendarClaimRoute => ["task.calendar-claim"],
            ShopPurchasePolicy.GoldRoute => ["task.gold-shop"],
            ShopPurchasePolicy.RaidRoute => ["task.raid-shop"],
            ShopPurchasePolicy.ExpeditionRoute => ["task.expedition-shop"],
            ResourceRefuelPolicy.GoldMineRoute => ["task.gold-mine-refuel"],
            ResourceRefuelPolicy.ResourceDrillRoute => ["task.resource-drill-refuel"],
            ResourceRefuelPolicy.CombinedRoute =>
                ["task.gold-mine-refuel", "task.resource-drill-refuel"],
            _ => throw new InvalidDataException($"Unknown Utility task route: {task.Route}."),
        };
        string mode = task.Mode switch
        {
            PlanTaskMode.Story => "mode.story",
            PlanTaskMode.Raid => "mode.raid",
            PlanTaskMode.Challenge => "mode.challenge",
            PlanTaskMode.Expedition => "mode.expedition",
            PlanTaskMode.Event => "mode.event",
            PlanTaskMode.Tower => "mode.story",
            _ => throw new ArgumentOutOfRangeException(nameof(task)),
        };
        return task.Mode == PlanTaskMode.Expedition &&
               !string.Equals(task.RewardTarget, "None", StringComparison.Ordinal)
            ? [mode, "feature.route-optimizer"]
            : [mode];
    }
}
