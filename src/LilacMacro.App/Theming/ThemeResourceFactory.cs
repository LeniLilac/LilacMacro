using System.Windows;
using System.Windows.Media;

namespace LilacMacro.App.Theming;

internal static class ThemeResourceFactory
{
    private static readonly string[] ColorKeys =
    [
        "Paper", "Card", "Ink", "InkSoft", "Muted", "Accent", "Pink", "OnAccent", "BadgeIcon",
        "ChromeBorder", "PressedSurface", "ScrollTrack", "ScrollThumb", "ScrollThumbHover", "Shadow", "AccentShadow",
    ];

    public static ResourceDictionary Create(AppTheme mode, AppColorTheme theme)
    {
        AppPaletteDefinition palette = AppPaletteCatalog.Get(theme, mode);
        bool light = mode == AppTheme.Light;
        Color accent = AppPaletteCatalog.Average(palette.Stops);
        Color paperBase = Parse(light ? "#FFF9FC" : "#141115");
        Color cardBase = Parse(light ? "#FFFEFF" : "#211B20");
        Color ink = Parse(light ? "#171116" : "#FFF5FA");
        Color inkSoft = Parse(light ? "#62505B" : "#C8AEBB");
        Color paper = Mix(paperBase, accent, light ? 0.045 : 0.075);
        Color card = Mix(cardBase, accent, light ? 0.018 : 0.065);
        Color muted = Mix(paperBase, accent, light ? 0.18 : 0.24);
        Color onAccent = ContrastInk(accent);
        Dictionary<string, Color> colors = new(StringComparer.Ordinal)
        {
            ["Paper"] = paper,
            ["Card"] = card,
            ["Ink"] = ink,
            ["InkSoft"] = inkSoft,
            ["Muted"] = muted,
            ["Accent"] = accent,
            ["Pink"] = accent,
            ["OnAccent"] = onAccent,
            ["BadgeIcon"] = onAccent,
            ["ChromeBorder"] = WithAlpha(ink, light ? 52 : 82),
            ["PressedSurface"] = Mix(muted, accent, light ? 0.26 : 0.34),
            ["ScrollTrack"] = WithAlpha(ink, light ? 36 : 53),
            ["ScrollThumb"] = WithAlpha(inkSoft, light ? 138 : 145),
            ["ScrollThumbHover"] = accent,
            ["Shadow"] = Parse(light ? "#FF171116" : "#FF090708"),
            ["AccentShadow"] = WithAlpha(accent, 102),
        };

        ResourceDictionary resources = new();
        foreach (string key in ColorKeys)
        {
            Color color = colors[key];
            resources[$"{key}Color"] = color;
            resources[$"{key}Brush"] = Freeze(new SolidColorBrush(color));
        }
        if (palette.Kind == AppPaletteKind.Gradient)
        {
            resources["AccentBrush"] = Freeze(AppPaletteCatalog.CreateGradient(palette.Stops));
            resources["PinkBrush"] = resources["AccentBrush"];
        }
        resources["AppBackgroundBrush"] = CreateBackgroundFill(palette, paper, light);
        resources["AppBackgroundPatternBrush"] = CreateBackgroundPattern(ink, light);
        resources["ThemePaletteOverlay"] = true;
        return resources;
    }

    public static Color ContrastInk(Color background)
    {
        double luminance = (0.2126 * Linear(background.R)) + (0.7152 * Linear(background.G)) + (0.0722 * Linear(background.B));
        return luminance > 0.46 ? Parse("#171116") : Colors.White;
    }

    private static Brush CreateBackgroundFill(AppPaletteDefinition palette, Color paper, bool light)
    {
        if (palette.Kind == AppPaletteKind.Gradient)
        {
            Color baseColor = light ? Colors.White : Parse("#0E0C0F");
            Color[] surfaceStops = palette.Stops.Select(color => Mix(baseColor, color, light ? 0.10 : 0.18)).ToArray();
            return Freeze(AppPaletteCatalog.CreateGradient(surfaceStops));
        }

        return Freeze(new SolidColorBrush(paper));
    }

    private static DrawingBrush CreateBackgroundPattern(Color ink, bool light)
    {
        const double dotSpacing = 16;
        const double lineSpacing = 80;
        const double patternHeight = 352;

        GeometryDrawing dot = new(
            new SolidColorBrush(WithAlpha(ink, light ? 34 : 42)),
            null,
            new EllipseGeometry(new Point(dotSpacing / 2, dotSpacing / 2), 1.35, 1.35));
        DrawingBrush dotPattern = new(dot)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, dotSpacing, dotSpacing),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, dotSpacing, dotSpacing),
            ViewboxUnits = BrushMappingMode.Absolute,
        };

        DrawingGroup group = new();
        group.Children.Add(new GeometryDrawing(
            dotPattern,
            null,
            new RectangleGeometry(new Rect(0, 0, lineSpacing, patternHeight))));
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(WithAlpha(ink, light ? 18 : 24)), 1),
            new LineGeometry(new Point(lineSpacing, 0), new Point(0, patternHeight))));
        DrawingBrush brush = new(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, lineSpacing, patternHeight),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, lineSpacing, patternHeight),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        return Freeze(brush);
    }

    private static Color Mix(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R + ((second.R - first.R) * amount)),
        (byte)Math.Round(first.G + ((second.G - first.G) * amount)),
        (byte)Math.Round(first.B + ((second.B - first.B) * amount)));

    private static Color WithAlpha(Color color, int alpha) => Color.FromArgb((byte)alpha, color.R, color.G, color.B);

    private static double Linear(byte component)
    {
        double value = component / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
