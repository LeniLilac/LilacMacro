using System.Windows;
using System.Windows.Media;

namespace LilacMacro.App.Theming;

internal sealed record AppPaletteSeed(
    AppColorTheme Theme,
    string Name,
    AppPaletteKind Kind,
    string[] LightStops,
    string[] DarkStops);

internal sealed record AppPaletteDefinition(
    AppColorTheme Theme,
    string Name,
    AppPaletteKind Kind,
    IReadOnlyList<Color> Stops);

internal sealed record ThemeSwatchOption(
    AppColorTheme Theme,
    string Name,
    AppPaletteKind Kind,
    Brush PreviewBrush,
    Brush CheckBrush,
    Brush CheckBackdropBrush);

internal static class AppPaletteCatalog
{
    private static readonly AppPaletteSeed[] Seeds =
    [
        Solid(AppColorTheme.Lilac, "Lilac", "#FF4FAC", "#FF70BD"),
        Solid(AppColorTheme.Rose, "Rose", "#EC407A", "#FF6B9E"),
        Solid(AppColorTheme.Coral, "Coral", "#F0645A", "#FF8379"),
        Solid(AppColorTheme.Amber, "Amber", "#E0A21A", "#FFC857"),
        Solid(AppColorTheme.Mint, "Mint", "#22A978", "#4AD9A6"),
        Solid(AppColorTheme.Teal, "Teal", "#1497A5", "#45C9D3"),
        Solid(AppColorTheme.Sky, "Sky", "#318ACF", "#65B8F2"),
        Solid(AppColorTheme.Cobalt, "Cobalt", "#4169D8", "#7695FF"),
        Solid(AppColorTheme.Violet, "Violet", "#8156D9", "#AB85F5"),
        Solid(AppColorTheme.Slate, "Slate", "#607486", "#91A5B6"),
        Gradient(AppColorTheme.Aurora, "Aurora", ["#42D6A4", "#7D6BFF"], ["#18A97B", "#8C5BFF"]),
        Gradient(AppColorTheme.Sunset, "Sunset", ["#FF6E70", "#FFBD52"], ["#D84C75", "#F08A3E"]),
        Gradient(AppColorTheme.Lagoon, "Lagoon", ["#12B7A6", "#4C9DF5"], ["#087B89", "#3158C8"]),
        Gradient(AppColorTheme.Prism, "Prism", ["#4D8DFF", "#A35BDF", "#F15BA6"], ["#275ED7", "#7B42BD", "#D23B8A"]),
        Gradient(AppColorTheme.Ember, "Ember", ["#E94654", "#F58C3D"], ["#A91F3C", "#D35B22"]),
        Gradient(AppColorTheme.Wisteria, "Wisteria", ["#9B72E8", "#F083C1"], ["#6842B7", "#BD3E8B"]),
        Gradient(AppColorTheme.Arctic, "Arctic", ["#68C7F2", "#75E0C1"], ["#247DAB", "#2BA98A"]),
        Gradient(AppColorTheme.Orchard, "Orchard", ["#7BBE55", "#FF9D78"], ["#477F31", "#CB604B"]),
        Gradient(AppColorTheme.Cosmos, "Cosmos", ["#3E56C5", "#E24FA9"], ["#202D88", "#A92B89"]),
        Gradient(AppColorTheme.Graphite, "Graphite", ["#667581", "#A07A8E"], ["#303942", "#765467"]),
    ];

    public static IReadOnlyList<AppColorTheme> Themes => Seeds.Select(seed => seed.Theme).ToArray();

    public static AppPaletteDefinition Get(AppColorTheme theme, AppTheme mode)
    {
        AppPaletteSeed seed = Seeds.FirstOrDefault(candidate => candidate.Theme == theme)
            ?? Seeds[0];
        string[] values = mode == AppTheme.Light ? seed.LightStops : seed.DarkStops;
        return new AppPaletteDefinition(seed.Theme, seed.Name, seed.Kind, values.Select(ParseColor).ToArray());
    }

    public static IReadOnlyList<ThemeSwatchOption> CreateSwatches(AppTheme mode) =>
        Seeds.Select(seed => CreateSwatch(Get(seed.Theme, mode))).ToArray();

    private static ThemeSwatchOption CreateSwatch(AppPaletteDefinition definition)
    {
        Brush preview = definition.Kind == AppPaletteKind.Gradient
            ? CreateGradient(definition.Stops)
            : new SolidColorBrush(definition.Stops[0]);
        Color representative = Average(definition.Stops);
        Color foreground = ThemeResourceFactory.ContrastInk(representative);
        return new ThemeSwatchOption(
            definition.Theme,
            $"{definition.Kind} · {definition.Name}",
            definition.Kind,
            Freeze(preview),
            Freeze(new SolidColorBrush(foreground)),
            Freeze(new SolidColorBrush(Color.FromArgb(220, foreground == Colors.White ? (byte)20 : (byte)255, foreground == Colors.White ? (byte)20 : (byte)255, foreground == Colors.White ? (byte)20 : (byte)255))));
    }

    internal static LinearGradientBrush CreateGradient(IReadOnlyList<Color> colors)
    {
        LinearGradientBrush brush = new() { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        for (int index = 0; index < colors.Count; index++)
            brush.GradientStops.Add(new GradientStop(colors[index], index / (double)(colors.Count - 1)));
        return brush;
    }

    internal static Color Average(IReadOnlyList<Color> colors) => Color.FromRgb(
        (byte)colors.Average(color => color.R),
        (byte)colors.Average(color => color.G),
        (byte)colors.Average(color => color.B));

    private static AppPaletteSeed Solid(AppColorTheme theme, string name, string light, string dark) =>
        new(theme, name, AppPaletteKind.Solid, [light], [dark]);

    private static AppPaletteSeed Gradient(AppColorTheme theme, string name, string[] light, string[] dark) =>
        new(theme, name, AppPaletteKind.Gradient, light, dark);

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
