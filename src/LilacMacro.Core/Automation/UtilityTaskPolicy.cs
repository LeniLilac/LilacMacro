namespace LilacMacro.Core.Automation;

public static class UtilityTaskPolicy
{
    public const string CalendarClaimRoute = "Calendar Claim";

    public static bool UsesFixedUtcReset(string route) =>
        ShopPurchasePolicy.IsShopRoute(route) ||
        string.Equals(route, CalendarClaimRoute, StringComparison.Ordinal);

    public static bool RequiresAreasMenu(string route) =>
        !string.Equals(route, CalendarClaimRoute, StringComparison.Ordinal);

    public static void Validate(string route, IReadOnlyList<string>? shopItemIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (ShopPurchasePolicy.IsShopRoute(route))
        {
            _ = ShopPurchasePolicy.ValidateSelection(route, shopItemIds ?? []);
            return;
        }
        if (string.Equals(route, CalendarClaimRoute, StringComparison.Ordinal)) return;
        _ = ResourceRefuelPolicy.TargetsFor(route);
    }

    public static DateTimeOffset NextDue(
        string route,
        DateTimeOffset completedAtUtc,
        int intervalMinutes)
    {
        if (ShopPurchasePolicy.IsShopRoute(route))
            return ShopPurchasePolicy.NextDue(route, completedAtUtc);
        if (string.Equals(route, CalendarClaimRoute, StringComparison.Ordinal))
            return ShopPurchasePolicy.NextUtcMidnight(completedAtUtc);
        _ = ResourceRefuelPolicy.TargetsFor(route);
        if (intervalMinutes < 1) throw new ArgumentOutOfRangeException(nameof(intervalMinutes));
        return completedAtUtc.AddMinutes(intervalMinutes);
    }

    public static string ScheduleLabel(string route, int intervalMinutes) => route switch
    {
        ShopPurchasePolicy.GoldRoute or CalendarClaimRoute => "Daily at 00:00 UTC",
        ShopPurchasePolicy.RaidRoute => "Weekly at 00:00 UTC",
        _ => $"Every {Math.Max(1, intervalMinutes)} min",
    };
}
