using System.Xml.Linq;
using LilacMacro.App.Runtime;

namespace LilacMacro.Tests;

public sealed class MacroDashboardLayoutTests
{
    [Fact]
    public void Run_log_yields_height_to_the_fixed_dock_surface()
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
        Assert.Equal("Auto", (string?)rows[1].Attribute("Height"));
        Assert.Equal("*", (string?)rows[2].Attribute("Height"));
        Assert.Equal("190", (string?)rows[2].Attribute("MaxHeight"));
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

        XElement dock = dashboard
            .Descendants(wpf + "Border")
            .Single(element => (string?)element.Attribute(x + "Name") == "DockCard");
        XElement stats = dashboard
            .Descendants(wpf + "Border")
            .Single(element => (string?)element.Attribute(x + "Name") == "StatsCard");
        Assert.Equal("766", (string?)dock.Attribute("Height"));
        Assert.Equal("Top", (string?)dock.Attribute("VerticalAlignment"));
        Assert.Equal("Top", (string?)stats.Attribute("VerticalAlignment"));

        XElement statsHeight = stats
            .Descendants(wpf + "Setter")
            .Single(element => (string?)element.Attribute("Property") == "Height"
                && (string?)element.Attribute("Value") == "766");
        Assert.NotNull(statsHeight);

        XElement tasksViewport = dashboard
            .Descendants(wpf + "Border")
            .Single(element => (string?)element.Attribute(x + "Name") == "UpcomingTasksViewport");
        Assert.Equal("True", (string?)tasksViewport.Attribute("ClipToBounds"));
    }

    [Fact]
    public void Window_minimum_preserves_each_workspace_profile()
    {
        Assert.Equal(
            (1788d, 898d),
            MacroDisplayPolicy.MinimumSize(MacroLayoutProfile.Full1920x1080));
        Assert.Equal(
            (1060d, 680d),
            MacroDisplayPolicy.MinimumSize(MacroLayoutProfile.Compact1366x768));
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
