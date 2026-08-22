using LilacMacro.Windows.SystemInformation;

namespace LilacMacro.Tests;

public sealed class WindowsVersionDescriptionTests
{
    [Fact]
    public void Format_replaces_environment_revision_with_registry_ubr()
    {
        string result = WindowsVersionDescription.Format(
            "Microsoft Windows NT 10.0.19045.0",
            new Version(10, 0, 19045, 0),
            6456);

        Assert.Equal("Microsoft Windows NT 10.0.19045.6456", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData("invalid")]
    public void Format_preserves_fallback_when_ubr_is_unavailable(object? updateBuildRevision)
    {
        const string fallback = "Microsoft Windows NT 10.0.19045.0";

        string result = WindowsVersionDescription.Format(
            fallback,
            new Version(10, 0, 19045, 0),
            updateBuildRevision);

        Assert.Equal(fallback, result);
    }
}
