using System.Xml.Linq;

namespace LilacMacro.Tests;

public sealed class SettingsInstallationIdentitySurfaceTests
{
    [Fact]
    public void GeneralSettingsExposeReadOnlyCopyableInstallationIdentity()
    {
        string repository = RepositoryRoot();
        string viewPath = Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "SettingsPage.xaml");
        XDocument view = XDocument.Load(viewPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement identity = Assert.Single(
            view.Descendants(),
            element => (string?)element.Attribute(x + "Name") == "InstallationIdText");
        Assert.Equal("True", (string?)identity.Attribute("IsReadOnly"));
        Assert.Single(
            view.Descendants(),
            element => (string?)element.Attribute("Click") == "CopyInstallationId_OnClick");

        string behavior = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "SettingsPage.InstallationIdentity.cs"));
        Assert.Contains("GetOrCreateAsync", behavior, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetText", behavior, StringComparison.Ordinal);
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
