using LilacMacro.Core.Imaging;

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
    double GreenFraction,
    double MainGrayFraction,
    double ExtensionGrayFraction);

public sealed record UnitPanelImageMatch(
    bool IsMatch,
    double PrioritySimilarity,
    double SellSimilarity);

public static class UnitPanelColorClassifier
{
    public const double MinimumReferenceSimilarity = 0.85;

    public static UnitUpgradeObservation ClassifyUpgrade(RgbImage main, RgbImage extension)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(extension);
        double green = Fraction(main, static (red, green, blue) =>
            green >= 75 && green - red >= 25 && green - blue >= 25);
        double mainGray = Fraction(main, IsControlGray);
        double extensionGray = Fraction(extension, IsControlGray);
        UnitUpgradeState state = green >= 0.30
            ? UnitUpgradeState.Affordable
            : mainGray < 0.50
                ? UnitUpgradeState.Unknown
                : extensionGray >= 0.75
                    ? UnitUpgradeState.Maxed
                    : extensionGray >= 0.25
                        ? UnitUpgradeState.Unaffordable
                        : UnitUpgradeState.Unknown;
        return new UnitUpgradeObservation(state, green, mainGray, extensionGray);
    }

    public static bool IsSelectedPanel(RgbImage priority, RgbImage sell)
    {
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(sell);
        double blue = Fraction(priority, static (red, green, blue) =>
            blue >= 80 && blue - red >= 20 && blue >= green && red <= 120);
        double redScore = Fraction(sell, static (red, green, blue) =>
            red >= 105 && red - green >= 35 && red - blue >= 25);
        return blue >= 0.18 && redScore >= 0.15;
    }

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
        bool matched = IsSelectedPanel(priority, sell)
            && prioritySimilarity >= MinimumReferenceSimilarity
            && sellSimilarity >= MinimumReferenceSimilarity;
        return new UnitPanelImageMatch(matched, prioritySimilarity, sellSimilarity);
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
