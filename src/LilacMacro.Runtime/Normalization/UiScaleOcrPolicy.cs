using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Runtime.Normalization;

internal sealed record SettingsSearchEvidence(PixelPoint SearchPoint, IReadOnlyList<string> Evidence);

internal sealed record UiScaleRowEvidence(
    PixelPoint ValuePoint,
    IReadOnlyList<string> Evidence);

internal static class UiScaleOcrPolicy
{
    private static readonly string[] NavigationLabels =
    [
        "all", "audio", "gameplay", "graphics", "units", "enemies", "miscellaneous", "keybinds", "testing",
    ];

    public static SettingsSearchEvidence? FindSettingsSearch(
        IReadOnlyList<OcrTextRegion> regions,
        UiScalePanelMatch panel)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (!panel.Visible || !panel.Settled) return null;

        OcrTextRegion? settings = Exact(regions, "settings")
            .Where(region => region.Bounds.Center.Y < panel.PanelBounds.Y + panel.PanelBounds.Height / 4)
            .OrderBy(region => region.Bounds.Y)
            .FirstOrDefault();
        int navigationMatches = NavigationLabels.Count(label =>
            Exact(regions, label).Any(region => region.Bounds.X < PanelRailLimit(panel)));
        if (settings is null || navigationMatches < 3) return null;

        OcrTextRegion? search = regions
            .Where(region =>
            {
                string text = OcrRuleEngine.Normalize(region.Text);
                return text.Contains("search", StringComparison.Ordinal) || text == "uiscale";
            })
            .Where(region =>
                region.Bounds.Center.Y < panel.PanelBounds.Y + panel.PanelBounds.Height * 0.22 &&
                region.Bounds.X > PanelRailLimit(panel))
            .OrderBy(region => region.Bounds.Y)
            .ThenByDescending(region => region.RecognitionConfidence)
            .FirstOrDefault();
        if (search is null) return null;

        return new SettingsSearchEvidence(
            search.Bounds.Center,
            ["SETTINGS", $"NAV {navigationMatches}", search.Text.Trim().ToUpperInvariant()]);
    }

    public static UiScaleRowEvidence? FindUiScaleRow(
        IReadOnlyList<OcrTextRegion> regions,
        UiScalePanelMatch panel)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (!panel.Visible || !panel.Settled) return null;

        OcrTextRegion? settings = Exact(regions, "settings").OrderBy(region => region.Bounds.Y).FirstOrDefault();
        OcrTextRegion? description = regions
            .Where(region => OcrRuleEngine.Normalize(region.Text).Contains("adjustthesizeofallui", StringComparison.Ordinal))
            .OrderByDescending(region => region.RecognitionConfidence)
            .FirstOrDefault();
        OcrTextRegion? elements = Exact(regions, "elements")
            .OrderBy(region => description is null ? double.MaxValue : Distance(region.Bounds.Center, description.Bounds.Center))
            .FirstOrDefault();
        OcrTextRegion? miscellaneous = Exact(regions, "miscellaneous")
            .Where(region => region.Bounds.X > PanelRailLimit(panel))
            .OrderBy(region => region.Bounds.Y)
            .FirstOrDefault();
        if (settings is null || description is null || elements is null || miscellaneous is null) return null;

        OcrTextRegion? label = Exact(regions, "uiscale")
            .Where(region => region.Bounds.Y <= description.Bounds.Y + description.Bounds.Height)
            .OrderBy(region => Distance(region.Bounds.Center, description.Bounds.Center))
            .FirstOrDefault();
        if (label is null) return null;

        return new UiScaleRowEvidence(
            ScaleValuePoint(panel.RenderedScale),
            ["SETTINGS", "MISCELLANEOUS", "UI SCALE", "ADJUST SIZE"]);
    }

    private static IEnumerable<OcrTextRegion> Exact(IReadOnlyList<OcrTextRegion> regions, string text) =>
        regions.Where(region => OcrRuleEngine.Normalize(region.Text) == text);

    private static int PanelRailLimit(UiScalePanelMatch panel) =>
        panel.PanelBounds.X + checked((int)Math.Round(panel.PanelBounds.Width * 0.21));

    private static PixelPoint ScaleValuePoint(double renderedScale) => new(
        checked((int)Math.Round(683 - 65 * renderedScale)),
        checked((int)Math.Round(350 - 137 * renderedScale)));

    private static double Distance(PixelPoint left, PixelPoint right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
