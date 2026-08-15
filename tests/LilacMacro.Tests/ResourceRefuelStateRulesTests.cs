using LilacMacro.App.Debugging;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class ResourceRefuelStateRulesTests
{
    [Fact]
    public void MineAndDrillRequireTheNewStableAddFuelOwner()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.GoldMineRefuel,
            Regions("Add Fuel")).IsMatch);
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ResourceDrillRefuel,
            Regions("AddFuel")).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.GoldMineRefuel,
            Regions("Fuel Cell", "Rewards")).IsMatch);
    }

    [Fact]
    public void DialogRequiresConfirmAndCancelOnSameRow()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.GoldMineRefuelConfirmation,
            Regions(("Confirm", 414), ("Cancel", 416))).IsMatch);
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ResourceDrillRefuelConfirmation,
            Regions(("Confirm", 395), ("Cancel", 393))).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.GoldMineRefuelConfirmation,
            Regions(("Confirm", 414), ("Cancel", 520))).IsMatch);
    }

    private static OcrTextRegion[] Regions(params string[] values) => values
        .Select((value, index) => Region(value, 220 + index * 30))
        .ToArray();

    private static OcrTextRegion[] Regions(params (string Text, int Y)[] values) => values
        .Select(value => Region(value.Text, value.Y))
        .ToArray();

    private static OcrTextRegion Region(string text, int y) => new()
    {
        Bounds = new PixelRect(400, y, 180, 20),
        Text = text,
        RecognitionConfidence = 0.99,
    };
}
