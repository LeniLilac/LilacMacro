using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class DebugWorkflowTargets
{
    public static readonly IReadOnlyList<OcrTargetRule> ResultSupport =
    [
        new("Repeat Stage", "repeat stage", "repeat"),
        new("View Party", "view party", "party"),
        new("Game Stats", "game stats"),
        new("Gained Rewards", "gained rewards"),
        new("Clear Time", "clear time"),
        new("Total Yen", "total yen"),
        new("Total Kills", "total kills"),
        new("Total Damage", "total damage"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> Lobby =
    [
        new("Store", "store"),
        new("Units", "units"),
        new("Items", "items"),
        new("Quests", "quests"),
        new("Summon", "summon"),
        new("Areas", "areas"),
        new("Play", "play"),
        new("Events", "events"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> Modes =
    [
        new("Story", "story", "progressive gamemode", "progressive"),
        new("Raid", "raid", "difficult gamemode", "difficult"),
        new("Challenge", "challenge", "reward gamemode", "reward"),
        new("Expedition", "expedition", "special gamemode", "special"),
        new("Tower", "tower", "tower mode"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> GoldShopSelector =
    [
        new("Gold Shop", "gold shop"),
        new("Event Shop", "event shop"),
        new("Leave", "leave"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> RaidShopSelector =
    [
        new("View Shop", "view shop"),
        new("Leave", "leave"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> GoldShop =
    [
        new("Gold Shop", "gold shop", "goldshop"),
        new("Cosmetic Shop", "cosmetic shop"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> RaidShop =
    [
        new("General", "general"),
        new("Spirit City", "spirit city"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ExpeditionShopSelector =
    [
        new(
            "Expedition Shop Description",
            "purchase useful items using expedition coins",
            "purchase useful items using expedition coin"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ExpeditionShop =
    [
        new("Back", "back"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ShopPurchaseDialog =
    [
        new("Buy Amount", "buy amount"),
        new("Purchase Question", "how much would you like to buy"),
        new("Cancel", "cancel"),
    ];
}
