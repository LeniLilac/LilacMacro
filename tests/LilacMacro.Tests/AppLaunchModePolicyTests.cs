using LilacMacro.App.Lifecycle;

namespace LilacMacro.Tests;

public sealed class AppLaunchModePolicyTests
{
    [Theory]
    [InlineData("--DATASET-BUILDER", "DatasetBuilder")]
    [InlineData("--RUNTIME-LAB", "RuntimeLab")]
    public void Resolve_WithToolArgumentReturnsRequestedMode(string argument, string expected)
    {
        AppLaunchMode result = AppLaunchModePolicy.Resolve(
            [argument],
            @"C:\Projects\LilacMacro\LilacMacro.exe");

        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("LilacMacro.DatasetBuilder.exe", "DatasetBuilder")]
    [InlineData("LilacMacro.RuntimeLab.exe", "RuntimeLab")]
    [InlineData("LilacMacro.exe", "Macro")]
    public void Resolve_FromExecutableNameReturnsExpectedMode(string executable, string expected)
    {
        AppLaunchMode result = AppLaunchModePolicy.Resolve(
            [],
            Path.Combine(@"C:\Projects\LilacMacro", executable));

        Assert.Equal(expected, result.ToString());
    }

    [Fact]
    public void Resolve_WithConflictingArgumentsRejectsAmbiguousShell()
    {
        Assert.Throws<ArgumentException>(() => AppLaunchModePolicy.Resolve(
            ["--dataset-builder", "--runtime-lab"],
            @"C:\Projects\LilacMacro\LilacMacro.exe"));
    }
}
