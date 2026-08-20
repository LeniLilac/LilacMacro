using System.Xml.Linq;

namespace LilacMacro.Tests;

public sealed class MacroDashboardLayoutTests
{
    [Fact]
    public void Run_log_is_content_sized_without_forcing_dashboard_height()
    {
        string repository = RepositoryRoot();
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument dashboard = XDocument.Load(Path.Combine(
            repository,
            "src",
            "LilacMacro.App",
            "Views",
            "MacroDashboardPage.xaml"));

        XElement root = dashboard
            .Descendants(wpf + "Grid")
            .Single(element => (string?)element.Attribute(x + "Name") == "DashboardRoot");
        XElement[] rows = root
            .Element(wpf + "Grid.RowDefinitions")!
            .Elements(wpf + "RowDefinition")
            .ToArray();

        Assert.Equal(3, rows.Length);
        Assert.Equal("Auto", (string?)rows[0].Attribute("Height"));
        Assert.Equal("3*", (string?)rows[1].Attribute("Height"));
        Assert.Equal("Auto", (string?)rows[2].Attribute("Height"));
        Assert.Null(rows[2].Attribute("MinHeight"));

        XElement logPanel = dashboard
            .Descendants(wpf + "Border")
            .Single(element => (string?)element.Attribute("Grid.Row") == "2");
        Assert.Null(logPanel.Attribute("MinHeight"));

        XElement log = dashboard
            .Descendants(wpf + "TextBox")
            .Single(element => (string?)element.Attribute(x + "Name") == "TraceLogText");
        Assert.Equal("Auto", (string?)log.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("140", (string?)log.Attribute("MaxHeight"));
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
