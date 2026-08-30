using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Placements;

public enum UnitUpgradeState
{
    Unknown,
    Unaffordable,
    Affordable,
    Maxed,
}

public sealed record UnitUpgradeObservation(
    UnitUpgradeState State,
    double PrimaryGreenFraction,
    double SecondaryGreenFraction,
    double PrimaryGrayFraction,
    double SecondaryGrayFraction);

public sealed record UnitPanelImageMatch(
    bool IsMatch,
    double PrioritySimilarity,
    double SellSimilarity,
    double PriorityBlueFraction,
    double PriorityRedFraction,
    double SellRedFraction,
    double SellBlueFraction);

public static class UnitPanelColorClassifier
{
    public const double MinimumReferenceSimilarity = 0.85;
    public const double MinimumUpgradeFillFraction = 0.70;
    public const double MinimumMaxedReferenceSimilarity = 0.90;
    public const double MinimumSelectedPriorityBlueFraction = 0.18;
    public const double MinimumSelectedSellRedFraction = 0.14;

    public static UnitUpgradeObservation ClassifyUpgrade(RgbImage primary, RgbImage secondary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);
        double primaryGreen = Fraction(primary, IsUpgradeGreen);
        double secondaryGreen = Fraction(secondary, IsUpgradeGreen);
        double primaryGray = Fraction(primary, IsControlGray);
        double secondaryGray = Fraction(secondary, IsControlGray);
        UnitUpgradeState state = primaryGreen >= MinimumUpgradeFillFraction &&
                                 secondaryGreen >= MinimumUpgradeFillFraction
            ? UnitUpgradeState.Affordable
            : primaryGray >= MinimumUpgradeFillFraction &&
              secondaryGray >= MinimumUpgradeFillFraction
                ? UnitUpgradeState.Unaffordable
                : UnitUpgradeState.Unknown;
        return new UnitUpgradeObservation(
            state, primaryGreen, secondaryGreen, primaryGray, secondaryGray);
    }

    public static bool IsMaxedText(string text) =>
        OcrPhraseMatcher.Normalize(text).Contains("maxed", StringComparison.Ordinal);

    public static bool MatchConfirmedMaxed(RgbImage reference, RgbImage candidate) =>
        Similarity(reference, candidate) >= MinimumMaxedReferenceSimilarity;

    private static bool IsUpgradeGreen(byte red, byte green, byte blue) =>
        green >= 75 && green - red >= 25 && green - blue >= 25;

    public static bool IsSelectedPanel(RgbImage priority, RgbImage sell)
        => AnalyzeSelectedPanel(priority, sell).IsMatch;

    private static UnitPanelImageMatch AnalyzeSelectedPanel(
        RgbImage priority,
        RgbImage sell,
        double prioritySimilarity = 0,
        double sellSimilarity = 0)
    {
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(sell);
        double priorityBlue = Fraction(priority, IsControlBlue);
        double priorityRed = Fraction(priority, IsControlRed);
        double sellRed = Fraction(sell, IsControlRed);
        double sellBlue = Fraction(sell, IsControlBlue);
        // Scaled field captures reached 0.149 red in the Sell crop and 0.145 blue overlap from
        // the adjacent Priority control. The independent blue Priority plus red Sell regions
        // still own visibility, so preserve both bounded UI-scale variations.
        bool matched = priorityBlue >= MinimumSelectedPriorityBlueFraction &&
                       sellRed >= MinimumSelectedSellRedFraction &&
                       priorityRed <= 0.12 && sellBlue <= 0.18;
        return new UnitPanelImageMatch(
            matched,
            prioritySimilarity,
            sellSimilarity,
            priorityBlue,
            priorityRed,
            sellRed,
            sellBlue);
    }

    private static bool IsControlBlue(byte red, byte green, byte blue) =>
        blue >= 80 && blue - red >= 20 && blue >= green && red <= 120;

    private static bool IsControlRed(byte red, byte green, byte blue) =>
        red >= 105 && red - green >= 35 && red - blue >= 25;

    public static UnitPanelImageMatch MatchSelectedPanel(
        RgbImage referencePriority,
        RgbImage referenceSell,
        RgbImage priority,
        RgbImage sell)
    {
        ArgumentNullException.ThrowIfNull(referencePriority);
        ArgumentNullException.ThrowIfNull(referenceSell);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(sell);
        double prioritySimilarity = Similarity(referencePriority, priority);
        double sellSimilarity = Similarity(referenceSell, sell);
        // The controls change text, price, highlight, and upgrade state while the panel remains
        // selected. Their independent blue-priority and red-sell fill regions own visibility;
        // whole-control similarity is retained only as diagnostic evidence.
        return AnalyzeSelectedPanel(priority, sell, prioritySimilarity, sellSimilarity);
    }

    private static bool IsControlGray(byte red, byte green, byte blue) =>
        Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) <= 12 &&
        red is >= 25 and <= 100;

    private static double Fraction(RgbImage image, Func<byte, byte, byte, bool> predicate)
    {
        int matches = 0;
        for (int index = 0; index < image.Pixels.Length; index += 3)
        {
            if (predicate(image.Pixels[index], image.Pixels[index + 1], image.Pixels[index + 2])) matches++;
        }
        return matches / (double)(image.Pixels.Length / 3);
    }

    private static double Similarity(RgbImage reference, RgbImage candidate)
    {
        if (reference.Size != candidate.Size) return 0;
        long difference = 0;
        for (int index = 0; index < reference.Pixels.Length; index++)
            difference += Math.Abs(reference.Pixels[index] - candidate.Pixels[index]);
        return 1 - difference / (reference.Pixels.Length * 255d);
    }
}
