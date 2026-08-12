using System.Windows;

namespace LilacMacro.App.Theming;

public static class AppThemeManager
{
    private const string ColorDictionaryMarker = "ThemeColors.";
    private const string OverlayMarker = "ThemePaletteOverlay";

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static AppColorTheme CurrentColorTheme { get; private set; } = AppColorTheme.Lilac;

    public static void Toggle() => Apply(
        Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light,
        CurrentColorTheme);

    public static void Apply(AppTheme theme) => Apply(theme, CurrentColorTheme);

    public static void Apply(AppTheme theme, AppColorTheme colorTheme)
    {
        if (!Enum.IsDefined(theme)) theme = AppTheme.Light;
        if (!Enum.IsDefined(colorTheme)) colorTheme = AppColorTheme.Lilac;
        IList<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary replacement = new()
        {
            Source = new Uri($"Themes/ThemeColors.{theme}.xaml", UriKind.Relative),
        };

        int colorIndex = FindColorDictionary(dictionaries);
        if (colorIndex < 0) dictionaries.Insert(0, replacement);
        else dictionaries[colorIndex] = replacement;

        ResourceDictionary overlay = ThemeResourceFactory.Create(theme, colorTheme);
        int overlayIndex = FindOverlayDictionary(dictionaries);
        if (overlayIndex < 0) dictionaries.Insert(colorIndex < 0 ? 1 : colorIndex + 1, overlay);
        else dictionaries[overlayIndex] = overlay;

        Current = theme;
        CurrentColorTheme = colorTheme;
    }

    private static int FindColorDictionary(IList<ResourceDictionary> dictionaries)
    {
        for (int index = 0; index < dictionaries.Count; index++)
        {
            string? source = dictionaries[index].Source?.OriginalString;
            if (source?.Contains(ColorDictionaryMarker, StringComparison.OrdinalIgnoreCase) == true) return index;
        }

        return -1;
    }

    private static int FindOverlayDictionary(IList<ResourceDictionary> dictionaries)
    {
        for (int index = 0; index < dictionaries.Count; index++)
        {
            if (dictionaries[index].Contains(OverlayMarker)) return index;
        }

        return -1;
    }
}
