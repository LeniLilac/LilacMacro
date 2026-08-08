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

public static class UnitPanelColorClassifier
{
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
}
