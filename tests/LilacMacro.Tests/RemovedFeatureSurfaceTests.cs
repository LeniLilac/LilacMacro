namespace LilacMacro.Tests;

public sealed class RemovedFeatureSurfaceTests
{
    [Fact]
    public void Settings_has_no_recording_or_manual_diagnostic_upload_surface()
    {
        string repository = RepositoryRoot();
        string settings = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "SettingsPage.xaml"));

        Assert.DoesNotContain("Manual diagnostic upload", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPLOAD ARCHIVE", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enable manual recording controls", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recording name", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_settings_has_no_recording_mode_controls()
    {
        string repository = RepositoryRoot();
        string timeline = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "PlacementTimelinePanel.xaml"));

        Assert.DoesNotContain("Recording mode start", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Playback start delay", timeline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Require start screen", timeline, StringComparison.OrdinalIgnoreCase);
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
