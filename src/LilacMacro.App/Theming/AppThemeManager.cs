using System.Windows;

namespace LilacMacro.App.Theming;

public static class AppThemeManager
{
    private const string ColorDictionaryMarker = "ThemeColors.";

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Toggle() => Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);

    public static void Apply(AppTheme theme)
    {
        IList<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary replacement = new()
        {
            Source = new Uri($"Themes/ThemeColors.{theme}.xaml", UriKind.Relative),
        };

        int colorIndex = FindColorDictionary(dictionaries);
        if (colorIndex < 0) dictionaries.Insert(0, replacement);
        else dictionaries[colorIndex] = replacement;

        Current = theme;
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
}
