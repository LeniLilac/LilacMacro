using System.Xml.Linq;

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
        Assert.DoesNotContain("Capture frames on failure", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Include current run log", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Deep Debug", settings, StringComparison.Ordinal);
        Assert.Contains("Enable Deep Debug Logs", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("CAPTURE INTERVAL, SEC", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("FRAME HISTORY, MIN", settings, StringComparison.Ordinal);
        Assert.Contains("MAX LOG STORAGE, GB", settings, StringComparison.Ordinal);
        Assert.Contains("OPEN DEEP DEBUG FOLDER", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Failure evidence", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CaptureIntervalCombo", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticsStatusText", settings, StringComparison.Ordinal);

        XDocument settingsDocument = XDocument.Load(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "SettingsPage.xaml"));
        XElement openDebugFolder = Assert.Single(
            settingsDocument.Descendants(),
            element => string.Equals(
                (string?)element.Attribute("Click"),
                "OpenDiagnostics_OnClick",
                StringComparison.Ordinal));
        Assert.Null(openDebugFolder.Attribute("Width"));

        string settingsCode = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "SettingsPage.xaml.cs"));
        Assert.DoesNotContain("DiagnosticsStatusText", settingsCode, StringComparison.Ordinal);
        Assert.Contains("DEEP DEBUG LOG SAVED", settingsCode, StringComparison.Ordinal);

        string privacyPanel = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "PrivacySettingsPanel.xaml"));
        string privacyPanelCode = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "PrivacySettingsPanel.xaml.cs"));
        Assert.DoesNotContain("Choices saved locally", privacyPanel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Choices saved locally", privacyPanelCode, StringComparison.OrdinalIgnoreCase);
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
