using LilacMacro.App.Workspace;

namespace LilacMacro.Tests;

public sealed class ToolShellProfileTests
{
    [Fact]
    public void DatasetBuilder_ContainsOnlyDatasetAuthoringPages()
    {
        ToolShellProfile profile = ToolShellProfile.Create(ToolShellKind.DatasetBuilder);

        Assert.Equal(PageKind.Capture, profile.StartPage);
        Assert.Equal([PageKind.Capture, PageKind.Review, PageKind.Datasets], profile.Pages);
        Assert.Equal("gpu:0", profile.OcrDevice);
        Assert.True(profile.KeepOcrLoaded);
        Assert.True(profile.PreloadOcrOnOpen);
    }

    [Fact]
    public void RuntimeLab_ContainsOnlyRuntimeTestPages()
    {
        ToolShellProfile profile = ToolShellProfile.Create(ToolShellKind.RuntimeLab);

        Assert.Equal(PageKind.Debug, profile.StartPage);
        Assert.Equal(
            [
                PageKind.Debug,
                PageKind.WireTest,
                PageKind.ScrollTest,
                PageKind.TeamSwapTest,
                PageKind.RouteOptimizerTest,
                PageKind.UtilityTaskTest,
            ],
            profile.Pages);
        Assert.Equal("gpu:0", profile.OcrDevice);
        Assert.True(profile.KeepOcrLoaded);
        Assert.True(profile.PreloadOcrOnOpen);
    }
}
