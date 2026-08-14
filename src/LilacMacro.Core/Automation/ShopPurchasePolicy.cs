using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Automation;

public enum ShopKind
{
    Gold,
    Raid,
    Expedition,
}

public sealed record ShopItemDefinition(string Id, string DisplayName, IReadOnlyList<string> OcrAliases);

public readonly record struct ShopPurchaseDialogActions(PixelPoint Maximum, PixelPoint Buy);

public static class ShopPurchasePolicy
{
    private const double ReferenceSelectorSeparation = 55d;
    private const double ReferenceSelectorAverageWidth = 100d;
    private const double ReferenceMaximumOffsetX = 86d;
    private const double ReferenceMaximumOffsetY = -61d;
    private const double ReferenceBuyOffsetX = -265d;
    private const double ReferenceBackWidth = 43d;
    private const double ReferenceCancelWidth = 62d;
    public const string GoldRoute = "Gold Shop";
    public const string RaidRoute = "Raid Shop";
    public const string ExpeditionRoute = "Expedition Shop";
    public const long RaidResetBeaconUnixSeconds = 1786579200;
    public const long ExpeditionResetBeaconUnixSeconds = 1786579200;
    public static readonly PixelRect ShopAreaRegion = new(377, 108, 543, 210);
    public static readonly PixelRect GoldSelectorRegion = new(362, 538, 618, 100);
    public static readonly PixelRect GoldHeaderRegion = new(209, 121, 292, 150);
    public static readonly PixelRect RaidSelectorRegion = new(391, 552, 567, 84);
    public static readonly PixelRect RaidHeaderRegion = new(202, 118, 306, 169);
    public static readonly PixelRect CatalogRegion = new(424, 134, 453, 498);
    public static readonly PixelRect ExpeditionCatalogRegion = new(623, 115, 709, 500);
    public static readonly PixelRect DialogRegion = new(414, 238, 541, 225);
    public static PixelPoint CatalogScrollPoint => CatalogRegion.Center;
    public static readonly PixelPoint HoverClearPoint = new(1341, 675);

    private static readonly ShopItemDefinition[] GoldItems =
    [
        Item("cursed-boba", "Cursed Boba"),
        Item("red-flower", "Red Flower"),
        Item("frown-fruit", "Frown Fruit"),
        Item("delicious-pie", "Delicious Pie"),
        Item("mana-flask", "Mana Flask"),
        Item("meat", "Meat"),
        Item("trait-crystal", "Trait Crystal"),
        Item("sprite-grey", "Sprite (Grey)", "sprite grey", "sprite (grey)"),
        Item("equipment-reroll", "Equipment Reroll"),
        Item("equipment-lock", "Equipment Lock"),
        Item("stat-reroll", "Stat Reroll"),
        Item("stat-lock", "Stat Lock"),
    ];

    private static readonly ShopItemDefinition[] RaidItems =
    [
        Item("sprite-grey", "Sprite (Grey)", "sprite grey", "sprite (grey)"),
        Item("trait-crystal", "Trait Crystal"),
        Item("stat-reroll", "Stat Reroll", "stat reroll", "3x stat reroll"),
        Item("stat-lock", "Stat Lock"),
        Item("equipment-reroll", "Equipment Reroll", "equipment reroll", "3x equipment reroll"),
        Item("equipment-lock", "Equipment Lock"),
    ];

    private static readonly ShopItemDefinition[] ExpeditionItems =
    [
        Item("gem", "Gem"),
        Item("gold", "Gold"),
        Item("stat-lock", "Stat Lock"),
        Item("stat-reroll", "Stat Reroll"),
        Item("trait-crystal", "Trait Crystal"),
        Item("katana", "Katana"),
        Item("equipment-reroll", "Equipment Reroll"),
        Item("equipment-lock", "Equipment Lock"),
        Item("futuristic-payload", "Futuristic Payload"),
    ];

    public static bool IsShopRoute(string route) =>
        string.Equals(route, GoldRoute, StringComparison.Ordinal) ||
        string.Equals(route, RaidRoute, StringComparison.Ordinal) ||
        string.Equals(route, ExpeditionRoute, StringComparison.Ordinal);

    public static ShopKind KindFor(string route) => route switch
    {
        GoldRoute => ShopKind.Gold,
        RaidRoute => ShopKind.Raid,
        ExpeditionRoute => ShopKind.Expedition,
        _ => throw new InvalidDataException($"Unknown shop utility route: {route}"),
    };

    public static IReadOnlyList<ShopItemDefinition> ItemsFor(string route) => KindFor(route) switch
    {
        ShopKind.Gold => GoldItems,
        ShopKind.Raid => RaidItems,
        ShopKind.Expedition => ExpeditionItems,
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    public static IReadOnlyList<ShopItemDefinition> ValidateSelection(
        string route,
        IEnumerable<string> selectedItemIds)
    {
        ArgumentNullException.ThrowIfNull(selectedItemIds);
        IReadOnlyList<ShopItemDefinition> available = ItemsFor(route);
        string[] selected = selectedItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0) throw new InvalidDataException($"{route} requires at least one item.");
        ShopItemDefinition[] resolved = selected
            .Select(id => available.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"{route} contains an unknown item: {id}"))
            .ToArray();
        return resolved;
    }

    public static DateTimeOffset NextDue(string route, DateTimeOffset completedAtUtc) => KindFor(route) switch
    {
        ShopKind.Gold => NextUtcMidnight(completedAtUtc),
        ShopKind.Raid => NextRaidReset(completedAtUtc),
        ShopKind.Expedition => NextExpeditionReset(completedAtUtc),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    public static DateTimeOffset NextUtcMidnight(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
    }

    public static DateTimeOffset NextRaidReset(DateTimeOffset value)
    {
        DateTimeOffset beacon = DateTimeOffset.FromUnixTimeSeconds(RaidResetBeaconUnixSeconds);
        DateTimeOffset utc = value.ToUniversalTime();
        if (utc < beacon) return beacon;
        long periods = ((utc - beacon).Ticks / TimeSpan.FromDays(7).Ticks) + 1;
        return beacon.AddDays(periods * 7);
    }

    public static DateTimeOffset NextExpeditionReset(DateTimeOffset value) =>
        NextBeaconReset(value, ExpeditionResetBeaconUnixSeconds, 2);

    public static PixelRect CatalogRegionFor(ShopKind kind) =>
        kind == ShopKind.Expedition ? ExpeditionCatalogRegion : CatalogRegion;

    public static PixelPoint CatalogScrollPointFor(ShopKind kind) => CatalogRegionFor(kind).Center;

    public static bool IsAvailableButton(RgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int green = 0;
        for (int index = 0; index < image.Pixels.Length; index += 3)
        {
            byte red = image.Pixels[index];
            byte currentGreen = image.Pixels[index + 1];
            byte blue = image.Pixels[index + 2];
            if (currentGreen >= 80 && currentGreen >= red * 1.35 && currentGreen >= blue * 1.2) green++;
        }
        return green >= image.Size.Width * image.Size.Height / 12;
    }

    public static bool TryResolveDialogActions(
        PixelRect primaryShopSelector,
        PixelRect secondaryShopSelector,
        PixelRect cancel,
        PixelSize clientSize,
        out ShopPurchaseDialogActions actions)
    {
        actions = default;
        if (!primaryShopSelector.IsInside(clientSize) ||
            !secondaryShopSelector.IsInside(clientSize) ||
            !cancel.IsInside(clientSize))
            return false;

        PixelPoint primary = primaryShopSelector.Center;
        PixelPoint secondary = secondaryShopSelector.Center;
        PixelPoint cancelCenter = cancel.Center;
        int selectorSeparation = secondary.Y - primary.Y;
        double selectorAverageWidth = (primaryShopSelector.Width + secondaryShopSelector.Width) / 2d;
        if (selectorSeparation is < 24 or > 72 ||
            Math.Abs(primary.X - secondary.X) > selectorSeparation ||
            selectorAverageWidth is < 55 or > 125 ||
            cancelCenter.X - primary.X is < 180 or > 650 ||
            cancelCenter.Y - secondary.Y is < 100 or > 300)
            return false;

        double renderedScale = (
            selectorSeparation / ReferenceSelectorSeparation +
            selectorAverageWidth / ReferenceSelectorAverageWidth) / 2d;
        if (renderedScale is < 0.55 or > 1.25) return false;
        PixelPoint maximum = new(
            cancelCenter.X + (int)Math.Round(ReferenceMaximumOffsetX * renderedScale),
            cancelCenter.Y + (int)Math.Round(ReferenceMaximumOffsetY * renderedScale));
        PixelPoint buy = new(
            cancelCenter.X + (int)Math.Round(ReferenceBuyOffsetX * renderedScale),
            cancelCenter.Y);
        if (!IsInside(DialogRegion, maximum) || !IsInside(DialogRegion, buy)) return false;
        actions = new ShopPurchaseDialogActions(maximum, buy);
        return true;
    }

    public static bool TryResolveExpeditionDialogActions(
        PixelRect back,
        PixelRect cancel,
        PixelSize clientSize,
        out ShopPurchaseDialogActions actions)
    {
        actions = default;
        if (!back.IsInside(clientSize) || !cancel.IsInside(clientSize)) return false;
        PixelPoint backCenter = back.Center;
        PixelPoint cancelCenter = cancel.Center;
        if (backCenter.X >= 250 || backCenter.Y <= 600 ||
            cancelCenter.X - backCenter.X is < 450 or > 900 ||
            back.Width is < 24 or > 70 || cancel.Width is < 34 or > 85)
            return false;

        double renderedScale = (back.Width / ReferenceBackWidth + cancel.Width / ReferenceCancelWidth) / 2d;
        if (renderedScale is < 0.55 or > 1.25) return false;
        PixelPoint maximum = new(
            cancelCenter.X + (int)Math.Round(ReferenceMaximumOffsetX * renderedScale),
            cancelCenter.Y + (int)Math.Round(ReferenceMaximumOffsetY * renderedScale));
        PixelPoint buy = new(
            cancelCenter.X + (int)Math.Round(ReferenceBuyOffsetX * renderedScale),
            cancelCenter.Y);
        if (!IsInside(DialogRegion, maximum) || !IsInside(DialogRegion, buy)) return false;
        actions = new ShopPurchaseDialogActions(maximum, buy);
        return true;
    }

    private static DateTimeOffset NextBeaconReset(DateTimeOffset value, long beaconSeconds, int days)
    {
        DateTimeOffset beacon = DateTimeOffset.FromUnixTimeSeconds(beaconSeconds);
        DateTimeOffset utc = value.ToUniversalTime();
        if (utc < beacon) return beacon;
        long periods = ((utc - beacon).Ticks / TimeSpan.FromDays(days).Ticks) + 1;
        return beacon.AddDays(periods * days);
    }

    private static bool IsInside(PixelRect bounds, PixelPoint point) =>
        point.X >= bounds.X && point.X < bounds.Right &&
        point.Y >= bounds.Y && point.Y < bounds.Bottom;

    private static ShopItemDefinition Item(string id, string name, params string[] aliases) =>
        new(id, name, aliases.Length == 0 ? [name] : aliases);
}
