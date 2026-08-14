using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Debugging;

internal sealed record RuntimeSearchRegionEvidence(
    string Owner,
    PixelRect Bounds,
    string Dataset,
    int Frame,
    string AnnotationLabel,
    string Intent);

internal static class RuntimeSearchRegionEvidenceCatalog
{
    public static readonly RuntimeSearchRegionEvidence SettingsClose = new(
        "UiScalePanelDetector.CloseSearch",
        new PixelRect(980, 32, 256, 162),
        "settings-ui-scale-p2-20260810-182357",
        1,
        "Settings Close Search",
        "Find the scale-relative red Settings close control.");

    public static readonly RuntimeSearchRegionEvidence SettingsGear = new(
        "UiScalePanelDetector.GearSearch",
        new PixelRect(210, 12, 41, 44),
        "settings-ui-scale-p2-20260810-182357",
        1,
        "Settings Gear Search",
        "Verify the closed-state Settings gear neighborhood.");

    public static readonly RuntimeSearchRegionEvidence SettingsGearGlyph = new(
        "UiScalePanelDetector.GearGlyphSearch",
        new PixelRect(218, 21, 25, 26),
        "settings-ui-scale-p2-20260810-182357",
        1,
        "Settings Gear Glyph Search",
        "Verify the white gear glyph inside the Settings control.");

    public static readonly RuntimeSearchRegionEvidence ExpeditionNodeBar = new(
        "ExpeditionNodeEvidenceService.BarBand",
        new PixelRect(330, 52, 700, 62),
        "expedition-node-set4-20260812-204347",
        1,
        "Node Bar Search",
        "Observe the current-node progress bar and node centers.");

    public static readonly RuntimeSearchRegionEvidence ExpeditionNodeHoverLine = new(
        "ExpeditionNodeEvidenceService.HoverLine",
        new PixelRect(300, 73, 746, 3),
        "expedition-node-set4-20260812-204347",
        1,
        "Hover Line",
        "Sweep left to right to discover the first live node hover target.");

    public static readonly RuntimeSearchRegionEvidence ExpeditionNodeTooltip = new(
        "ExpeditionNodeEvidenceService.TooltipTitleBand",
        new PixelRect(348, 61, 660, 55),
        "expedition-node-set4-20260812-204347",
        1,
        "Node Tooltip Search",
        "Read the complete node hover title across recorded UI scales.");

    public static readonly RuntimeSearchRegionEvidence ExpeditionRewards = new(
        "ExpeditionRewardPoolService.RewardStrip",
        new PixelRect(0, 565, 1356, 135),
        "expedition-route-reward-pool-20260809-195831",
        1,
        "Route Reward Search",
        "Read the route reward cards and Back action.");

    public static readonly RuntimeSearchRegionEvidence RestartConfirmation = new(
        "ExpeditionSettingsService.RestartDialog",
        new PixelRect(430, 250, 510, 210),
        "restart-via-settings-set3-20260812-223338",
        6,
        "Restart Confirmation Search",
        "Read the paired Restart and Cancel actions.");

    public static readonly RuntimeSearchRegionEvidence ExpeditionShopCatalog = new(
        "ShopPurchaseService.ExpeditionCatalogRegion",
        new PixelRect(623, 115, 709, 500),
        "expediton-shop-scroll-multi-ui-scale-20260814-083856",
        1,
        "Shop+Scroll Area",
        "Read and scroll the three-column Expedition Shop catalog.");

    public static IReadOnlyList<RuntimeSearchRegionEvidence> All { get; } =
    [
        SettingsClose,
        SettingsGear,
        SettingsGearGlyph,
        ExpeditionNodeBar,
        ExpeditionNodeHoverLine,
        ExpeditionNodeTooltip,
        ExpeditionRewards,
        RestartConfirmation,
        ExpeditionShopCatalog,
    ];
}
