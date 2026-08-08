using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Placements;

public sealed record UnitPanelLayout(
    PixelRect PriorityText,
    PixelRect SellText,
    PixelRect DpsText,
    PixelRect PriorityControl,
    PixelRect SellControl,
    PixelRect UpgradeControl,
    PixelRect UpgradeMain,
    PixelRect UpgradeExtension,
    PixelRect AutoUpgradeControl,
    double Scale)
{
    public static UnitPanelLayout? TryCreate(IReadOnlyList<OcrTextRegion> regions, PixelSize clientSize)
    {
        ArgumentNullException.ThrowIfNull(regions);
        OcrTextRegion? priority = Find(regions, "priority", "prlorlty", "priortty");
        OcrTextRegion? sell = Find(regions, "sell");
        OcrTextRegion? dps = regions
            .Where(region => OcrPhraseMatcher.Normalize(region.Text).StartsWith("dps", StringComparison.Ordinal))
            .OrderByDescending(region => region.Bounds.Y)
            .FirstOrDefault();
        if (priority is null || sell is null || dps is null) return null;

        double unit = sell.Bounds.Center.X - priority.Bounds.Center.X;
        if (unit < 45 || unit > clientSize.Width * 0.15) return null;
        int height = Math.Max(priority.Bounds.Height, sell.Bounds.Height);
        int top = Math.Max(0, Math.Min(priority.Bounds.Y, sell.Bounds.Y) - Scaled(height, 0.75));
        int bottom = Math.Min(clientSize.Height, Math.Max(priority.Bounds.Bottom, sell.Bounds.Bottom) + Scaled(height, 0.75));
        int controlHeight = bottom - top;
        int priorityLeft = Math.Max(0, priority.Bounds.Center.X - Scaled(unit, 0.58));
        int sellLeft = Math.Max(0, sell.Bounds.Center.X - Scaled(unit, 0.64));
        int upgradeLeft = sell.Bounds.Center.X + Scaled(unit, 0.36);
        int upgradeRight = Math.Min(clientSize.Width, sell.Bounds.Center.X + Scaled(unit, 2.12));
        int extensionWidth = Math.Max(4, Scaled(unit, 0.51));
        int extensionLeft = Math.Max(upgradeLeft + 1, upgradeRight - extensionWidth);

        PixelRect upgrade = new(upgradeLeft, top, upgradeRight - upgradeLeft, controlHeight);
        return new UnitPanelLayout(
            priority.Bounds,
            sell.Bounds,
            dps.Bounds,
            new PixelRect(priorityLeft, top, Math.Max(4, Scaled(unit, 0.86)), controlHeight),
            new PixelRect(sellLeft, top, Math.Max(4, Scaled(unit, 0.65)), controlHeight),
            upgrade,
            new PixelRect(upgradeLeft, top, extensionLeft - upgradeLeft, controlHeight),
            new PixelRect(extensionLeft, top, upgradeRight - extensionLeft, controlHeight),
            new PixelRect(extensionLeft, top, upgradeRight - extensionLeft, controlHeight),
            unit / 104.5);
    }

    public bool IsCloseTo(UnitPanelLayout other)
    {
        ArgumentNullException.ThrowIfNull(other);
        int tolerance = Math.Max(2, (int)Math.Ceiling(Math.Max(Scale, other.Scale) * 3));
        return Close(PriorityText, other.PriorityText, tolerance) &&
            Close(SellText, other.SellText, tolerance) &&
            Close(UpgradeControl, other.UpgradeControl, tolerance);
    }

    public static bool IsPhysicalDps(string text)
    {
        string normalized = OcrPhraseMatcher.Normalize(text);
        if (!normalized.StartsWith("dps", StringComparison.Ordinal) || normalized.Contains("???", StringComparison.Ordinal))
            return false;
        string suffix = normalized[3..];
        return suffix.EndsWith('s') && suffix[..^1].Any(character => char.IsAsciiDigit(character) || character is 'q' or 'o');
    }

    public static bool IsPhantomDps(string text) => text.Contains("???", StringComparison.Ordinal);

    private static OcrTextRegion? Find(IReadOnlyList<OcrTextRegion> regions, params string[] aliases) => regions
        .Where(region => aliases.Contains(OcrPhraseMatcher.Normalize(region.Text), StringComparer.Ordinal))
        .OrderByDescending(region => region.Bounds.Y)
        .FirstOrDefault();

    private static int Scaled(double value, double factor) => Math.Max(1, (int)Math.Round(value * factor));

    private static bool Close(PixelRect first, PixelRect second, int tolerance) =>
        Math.Abs(first.X - second.X) <= tolerance && Math.Abs(first.Y - second.Y) <= tolerance &&
        Math.Abs(first.Width - second.Width) <= tolerance && Math.Abs(first.Height - second.Height) <= tolerance;
}

public sealed class UnitPanelLayoutTracker(int requiredStableObservations = 3)
{
    private UnitPanelLayout? _candidate;
    private int _stable;

    public UnitPanelLayout? Observe(UnitPanelLayout? observation)
    {
        if (observation is null)
        {
            _candidate = null;
            _stable = 0;
            return null;
        }
        if (_candidate is not null && _candidate.IsCloseTo(observation)) _stable++;
        else
        {
            _candidate = observation;
            _stable = 1;
        }
        return _stable >= requiredStableObservations ? _candidate : null;
    }
}
