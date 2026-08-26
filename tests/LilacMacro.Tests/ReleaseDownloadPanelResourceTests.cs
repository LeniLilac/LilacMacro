namespace LilacMacro.Tests;

public sealed class ReleaseDownloadPanelResourceTests
{
    [Fact]
    public void Parent_scoped_button_style_is_resolved_after_panel_is_attached()
    {
        string xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "LilacMacro.App",
            "Views",
            "ReleaseDownloadPanel.xaml"));

        Assert.Contains(
            "Style=\"{DynamicResource SettingsFieldActionButtonStyle}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Style=\"{StaticResource SettingsFieldActionButtonStyle}\"",
            xaml,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "runtime-evidence.json")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the LilacMacro repository root.");
    }
}
