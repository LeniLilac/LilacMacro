using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Ocr;

public enum AreaCategory
{
    Upgrade,
    Gamemode,
    Lobby,
    Shop,
    Expedition,
}

public static class AreaSelectionRules
{
    private static readonly OcrTargetRule Areas = new("Areas", "areas");
    private static readonly OcrTargetRule Upgrade = new("Upgrade", "upgrade");
    private static readonly OcrTargetRule Gamemode = new("Gamemode", "gamemode");
    private static readonly OcrTargetRule Lobby = new("Lobby", "lobby");
    private static readonly OcrTargetRule Shop = new("Shop", "shop");
    private static readonly OcrTargetRule Expedition = new("Expedition", "expedition");

    public static IReadOnlyList<OcrTargetRule> StateTargets { get; } =
    [
        Areas,
        Upgrade,
        Gamemode,
        Lobby,
        Shop,
        Expedition,
    ];

    public static OcrTargetRule TargetFor(AreaCategory category) => category switch
    {
        AreaCategory.Upgrade => Upgrade,
        AreaCategory.Gamemode => Gamemode,
        AreaCategory.Lobby => Lobby,
        AreaCategory.Shop => Shop,
        AreaCategory.Expedition => Expedition,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static OcrTargetMatch? Find(
        AreaCategory category,
        IReadOnlyList<OcrTextRegion> regions)
    {
        OcrTargetMatch? title = OcrRuleEngine.FindExactTarget(Areas, regions);
        OcrTargetMatch? target = OcrRuleEngine.FindLeftmostTarget(TargetFor(category), regions);
        if (title is null || target is null) return null;

        int navigationMaximumX = checked(title.Region.Bounds.Right + title.Region.Bounds.Width);
        return target.Region.Bounds.Center.X <= navigationMaximumX ? target : null;
    }
}
