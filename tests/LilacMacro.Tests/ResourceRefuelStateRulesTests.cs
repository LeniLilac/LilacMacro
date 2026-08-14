using LilacMacro.App.Debugging;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class ResourceRefuelStateRulesTests
{
    [Fact]
    public void GoldMineRequiresStationIdentityAndIndependentPanelEvidence()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.GoldMineRefuel,
            Regions("Fuel Cell", "Rewards", "Put fuel to start generating Gold!", "Add Fuel")).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.GoldMineRefuel,
            Regions("Fuel Cell", "Rewards", "start mining for geodes", "Add Fuel")).IsMatch);
    }

    [Fact]
    public void DrillAcceptsObservedOcrVariationButRejectsMine()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ResourceDrillRefuel,
            Regions("Fuel Cell", "Rewards", "orill to start mining for", "AddFuel")).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ResourceDrillRefuel,
            Regions("Fuel Cell", "Rewards", "start generating Gold", "Add Fuel")).IsMatch);
    }

    [Fact]
    public void DialogRequiresConfirmAndCancelOnSameRow()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.AddFuelDialog,
            Regions(("Add Fuel", 250), ("Confirm", 414), ("Cancel", 416))).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.AddFuelDialog,
            Regions(("Add Fuel", 250), ("Confirm", 414), ("Cancel", 520))).IsMatch);
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
