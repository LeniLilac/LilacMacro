using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Tests;

public sealed class ShopPurchasePolicyTests
{
    [Fact]
    public void RoutesExposeIndependentSupportedCatalogs()
    {
        Assert.Equal(12, ShopPurchasePolicy.ItemsFor(ShopPurchasePolicy.GoldRoute).Count);
        Assert.Equal(6, ShopPurchasePolicy.ItemsFor(ShopPurchasePolicy.RaidRoute).Count);
        Assert.DoesNotContain(
            ShopPurchasePolicy.ItemsFor(ShopPurchasePolicy.RaidRoute),
            item => item.Id == "cursed-boba");
    }

    [Fact]
    public void SelectionRequiresKnownDistinctItems()
    {
        Assert.Equal(2, ShopPurchasePolicy.ValidateSelection(
            ShopPurchasePolicy.GoldRoute,
            ["trait-crystal", "trait-crystal", "equipment-lock"]).Count);
        Assert.Throws<InvalidDataException>(() =>
            ShopPurchasePolicy.ValidateSelection(ShopPurchasePolicy.GoldRoute, []));
        Assert.Throws<InvalidDataException>(() =>
            ShopPurchasePolicy.ValidateSelection(ShopPurchasePolicy.RaidRoute, ["cursed-boba"]));
    }

    [Fact]
    public void GoldResetIsNextUtcMidnight()
    {
        DateTimeOffset completed = new(2026, 8, 13, 23, 59, 0, TimeSpan.Zero);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
            ShopPurchasePolicy.NextDue(ShopPurchasePolicy.GoldRoute, completed));
    }

    [Fact]
    public void RaidResetUsesSevenDayBeaconEpoch()
    {
        DateTimeOffset beacon = DateTimeOffset.FromUnixTimeSeconds(ShopPurchasePolicy.RaidResetBeaconUnixSeconds);
        Assert.Equal(beacon, ShopPurchasePolicy.NextDue(ShopPurchasePolicy.RaidRoute, beacon.AddSeconds(-1)));
        Assert.Equal(beacon.AddDays(7), ShopPurchasePolicy.NextDue(ShopPurchasePolicy.RaidRoute, beacon));
        Assert.Equal(beacon.AddDays(14), ShopPurchasePolicy.NextDue(ShopPurchasePolicy.RaidRoute, beacon.AddDays(7)));
    }

    [Fact]
    public void AvailabilityRequiresMeaningfulGreenButtonArea()
    {
        byte[] green = Enumerable.Repeat(new byte[] { 40, 180, 30 }, 120).SelectMany(value => value).ToArray();
        byte[] gray = Enumerable.Repeat(new byte[] { 90, 90, 90 }, 120).SelectMany(value => value).ToArray();
        Assert.True(ShopPurchasePolicy.IsAvailableButton(new RgbImage(12, 10, green)));
        Assert.False(ShopPurchasePolicy.IsAvailableButton(new RgbImage(12, 10, gray)));
    }

    [Fact]
    public void CatalogScrollPointIsTheFixedScrollableItemRegionCenter()
    {
        Assert.Equal(ShopPurchasePolicy.CatalogRegion.Center, ShopPurchasePolicy.CatalogScrollPoint);
        Assert.Equal(new PixelPoint(650, 383), ShopPurchasePolicy.CatalogScrollPoint);
        Assert.Equal(new PixelPoint(1341, 675), ShopPurchasePolicy.HoverClearPoint);
    }

    [Theory]
    [MemberData(nameof(DialogScaleSamples))]
    public void DialogActionsDeriveUnobservedButtonsAcrossThreeRenderedScales(
        PixelRect primary,
        PixelRect secondary,
        PixelRect cancel,
        PixelRect maximumButton,
        PixelRect buyButton,
        int minimumExpectedMargin)
    {
        PixelSize client = new(1366, 700);
        Assert.True(ShopPurchasePolicy.TryResolveDialogActions(
            primary,
            secondary,
            cancel,
            client,
            out ShopPurchaseDialogActions actions));
        Assert.True(Contains(maximumButton, actions.Maximum));
        Assert.True(Contains(buyButton, actions.Buy));
        Assert.True(EdgeMargin(maximumButton, actions.Maximum) >= minimumExpectedMargin);
        Assert.True(EdgeMargin(buyButton, actions.Buy) >= minimumExpectedMargin);
    }

    [Fact]
    public void DialogActionsRejectTargetsOutsideStableDialogLayout()
    {
        PixelSize client = new(1366, 700);
        Assert.False(ShopPurchasePolicy.TryResolveDialogActions(
            new PixelRect(267, 139, 82, 25),
            new PixelRect(257, 150, 102, 24),
            new PixelRect(785, 414, 62, 26),
            client,
            out _));
    }

    public static TheoryData<PixelRect, PixelRect, PixelRect, PixelRect, PixelRect, int> DialogScaleSamples => new()
    {
        {
            new PixelRect(266, 135, 84, 28), new PixelRect(249, 192, 116, 24),
            new PixelRect(787, 415, 58, 22), new PixelRect(869, 345, 67, 39),
            new PixelRect(422, 404, 259, 49), 18
        },
        {
            new PixelRect(335, 172, 72, 24), new PixelRect(322, 219, 98, 21),
            new PixelRect(769, 405, 50, 18), new PixelRect(838, 345, 56, 37),
            new PixelRect(465, 394, 216, 42), 18
        },
        {
            new PixelRect(403, 205, 62, 24), new PixelRect(393, 241, 82, 20),
            new PixelRect(751, 393, 40, 16), new PixelRect(807, 347, 44, 27),
            new PixelRect(505, 384, 176, 38), 12
        },
    };

    private static bool Contains(PixelRect bounds, PixelPoint point) =>
        point.X >= bounds.X && point.X < bounds.Right &&
        point.Y >= bounds.Y && point.Y < bounds.Bottom;

    private static int EdgeMargin(PixelRect bounds, PixelPoint point) =>
        Math.Min(
            Math.Min(point.X - bounds.X, bounds.Right - 1 - point.X),
            Math.Min(point.Y - bounds.Y, bounds.Bottom - 1 - point.Y));

    [Fact]
    public void UtilityScheduleUsesFixedResetsOnlyForCalendarAndShops()
    {
        DateTimeOffset completed = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(completed.AddHours(12), UtilityTaskPolicy.NextDue(
            UtilityTaskPolicy.CalendarClaimRoute, completed, 5));
        Assert.Equal(completed.AddMinutes(5), UtilityTaskPolicy.NextDue(
            ResourceRefuelPolicy.GoldMineRoute, completed, 5));
        Assert.Equal("Daily at 00:00 UTC", UtilityTaskPolicy.ScheduleLabel(
            ShopPurchasePolicy.GoldRoute, 1));
    }

    [Fact]
    public void CalendarRequiresNoShopSelection()
    {
        UtilityTaskPolicy.Validate(UtilityTaskPolicy.CalendarClaimRoute, []);
        Assert.Throws<InvalidDataException>(() => UtilityTaskPolicy.Validate(
            ShopPurchasePolicy.GoldRoute, []));
    }
}
