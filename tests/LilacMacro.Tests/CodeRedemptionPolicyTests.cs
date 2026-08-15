using LilacMacro.App.Debugging;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class CodeRedemptionPolicyTests
{
    [Fact]
    public void LauncherPoint_UsesRecordedGearRelativeOffset()
    {
        Assert.Equal(
            new PixelPoint(237, 34),
            CodeRedemptionPolicy.LauncherPoint(
                new PixelPoint(276, 34),
                DebugWorkflowCatalog.ClientSize));
    }

    [Fact]
    public void LauncherPoint_RejectsUnsafeGeometry()
    {
        Assert.Throws<InvalidDataException>(() => CodeRedemptionPolicy.LauncherPoint(
            new PixelPoint(20, 34),
            DebugWorkflowCatalog.ClientSize));
    }

    [Fact]
    public void Catalog_UsesSeparateDatasetOwnedStates()
    {
        Assert.Equal("Codes Launcher State", DebugCodeWorkflowCatalog.Launcher.RegionLabel);
        Assert.Equal("Codes Panel State", DebugCodeWorkflowCatalog.Panel.RegionLabel);
        Assert.NotEqual(
            DebugCodeWorkflowCatalog.Launcher.DatasetDirectory,
            DebugCodeWorkflowCatalog.Panel.DatasetDirectory);
        Assert.Equal(DebugMatchMode.ExactTargets, DebugCodeWorkflowCatalog.Launcher.MatchMode);
        Assert.Equal(DebugMatchMode.ExactTargets, DebugCodeWorkflowCatalog.Panel.MatchMode);
    }

    [Fact]
    public void Launcher_RequiresAllThreeIndependentControls()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugCodeWorkflowCatalog.Launcher,
            Regions("Join Friend", "Redeem Codes", "Lobby Music")).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugCodeWorkflowCatalog.Launcher,
            Regions("Join Friend", "Redeem Codes")).IsMatch);
    }

    [Fact]
    public void Panel_RequiresHeaderAndRedeemAction()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugCodeWorkflowCatalog.Panel,
            Regions("Codes", "Enter Code...", "Redeem Code")).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugCodeWorkflowCatalog.Panel,
            Regions("Codes", "Enter Code...")).IsMatch);
    }

    private static OcrTextRegion[] Regions(params string[] text) => text
        .Select((value, index) => new OcrTextRegion
        {
            Bounds = new PixelRect(20, 20 + index * 30, 200, 20),
            Text = value,
            RecognitionConfidence = 0.99,
        })
        .ToArray();
}
