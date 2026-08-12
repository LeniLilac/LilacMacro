using System.Windows.Media;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Theming;

namespace LilacMacro.Tests;

public sealed class AppThemeTests
{
    [Fact]
    public void CatalogContainsTenSolidAndTenGradientFamilies()
    {
        AppPaletteDefinition[] definitions = AppPaletteCatalog.Themes
            .Select(theme => AppPaletteCatalog.Get(theme, AppTheme.Light))
            .ToArray();

        Assert.Equal(20, definitions.Length);
        Assert.Equal(10, definitions.Count(item => item.Kind == AppPaletteKind.Solid));
        Assert.Equal(10, definitions.Count(item => item.Kind == AppPaletteKind.Gradient));
        Assert.Equal(20, definitions.Select(item => item.Theme).Distinct().Count());
        Assert.All(
            AppPaletteCatalog.Themes,
            theme => Assert.NotEqual(Signature(theme, AppTheme.Light), Signature(theme, AppTheme.Dark)));
    }

    [Fact]
    public void EveryGradientIsUniqueWithinEachModeAndHasADistinctCounterpart()
    {
        AppColorTheme[] gradients = AppPaletteCatalog.Themes
            .Where(theme => AppPaletteCatalog.Get(theme, AppTheme.Light).Kind == AppPaletteKind.Gradient)
            .ToArray();
        string[] light = gradients.Select(theme => Signature(theme, AppTheme.Light)).ToArray();
        string[] dark = gradients.Select(theme => Signature(theme, AppTheme.Dark)).ToArray();

        Assert.Equal(10, light.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(10, dark.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryVariantBuildsACompleteLiveOverlay()
    {
        string[] required =
        [
            "PaperColor", "PaperBrush", "CardColor", "CardBrush", "InkColor", "InkBrush",
            "InkSoftColor", "InkSoftBrush", "MutedColor", "MutedBrush", "AccentColor", "AccentBrush",
            "PinkColor", "PinkBrush", "OnAccentColor", "OnAccentBrush", "BadgeIconColor", "BadgeIconBrush",
            "ChromeBorderColor", "ChromeBorderBrush", "PressedSurfaceColor", "PressedSurfaceBrush",
            "ScrollTrackColor", "ScrollTrackBrush", "ScrollThumbColor", "ScrollThumbBrush",
            "ScrollThumbHoverColor", "ScrollThumbHoverBrush", "ShadowColor", "ShadowBrush",
            "AccentShadowColor", "AccentShadowBrush", "AppBackgroundBrush", "ThemePaletteOverlay",
        ];

        foreach (AppColorTheme theme in AppPaletteCatalog.Themes)
        {
            foreach (AppTheme mode in Enum.GetValues<AppTheme>())
            {
                System.Windows.ResourceDictionary resources = ThemeResourceFactory.Create(mode, theme);
                Assert.All(required, key => Assert.True(resources.Contains(key), $"{mode}/{theme} omitted {key}."));
                AppPaletteDefinition definition = AppPaletteCatalog.Get(theme, mode);
                if (definition.Kind == AppPaletteKind.Gradient)
                    Assert.IsType<LinearGradientBrush>(resources["AccentBrush"]);
                else
                    Assert.IsType<SolidColorBrush>(resources["AccentBrush"]);
            }
        }
    }

    [Fact]
    public async Task AppearanceRoundTripsAndSchemaEightMigratesToLilacLight()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            first.SetAppearance(AppTheme.Dark, AppColorTheme.Cosmos);
            await first.FlushAsync();

            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.Equal(AppTheme.Dark, restored.ThemeMode);
            Assert.Equal(AppColorTheme.Cosmos, restored.ColorTheme);

            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "macro-settings.json"),
                """
                {
                  "schema_version": 8
                }
                """);
            MacroSettings migrated = await new MacroSettingsStore(root).LoadAsync();
            Assert.Equal(MacroSettings.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.Equal(AppTheme.Light, migrated.ThemeMode);
            Assert.Equal(AppColorTheme.Lilac, migrated.ColorTheme);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string Signature(AppColorTheme theme, AppTheme mode) => string.Join(
        '/',
        AppPaletteCatalog.Get(theme, mode).Stops.Select(color => color.ToString()));
}
