using System.Xml.Linq;

namespace LilacMacro.Tests;

public sealed class PrivacyChoicesLayoutTests
{
    [Fact]
    public void First_run_choices_use_the_shared_button_styles_without_extra_intro_copy()
    {
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument window = XDocument.Load(Path.Combine(
            RepositoryRoot(),
            "src",
            "LilacMacro.App",
            "Views",
            "PrivacyChoicesWindow.xaml"));
        string markup = window.ToString();

        Assert.DoesNotContain("Choose how LilacMacro connects", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Discord reporting is configured separately", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("GPU OCR setup screen", markup, StringComparison.Ordinal);

        XElement[] buttons = window.Descendants(wpf + "Button").ToArray();
        Assert.Equal(
            "{StaticResource ButtonStyle}",
            (string?)buttons.Single(button => (string?)button.Attribute("Content") == "PRIVACY")
                .Attribute("Style"));
        Assert.Equal(
            "{StaticResource ButtonStyle}",
            (string?)buttons.Single(button => (string?)button.Attribute("Content") == "TERMS")
                .Attribute("Style"));
        Assert.Equal(
            "{StaticResource PrimaryButtonStyle}",
            (string?)buttons.Single(button => (string?)button.Attribute("Content") == "SAVE & CONTINUE")
                .Attribute("Style"));

        XElement saveError = window
            .Descendants(wpf + "TextBlock")
            .Single(element => (string?)element.Attribute(x + "Name") == "SaveErrorText");
        Assert.Equal("Collapsed", (string?)saveError.Attribute("Visibility"));
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
