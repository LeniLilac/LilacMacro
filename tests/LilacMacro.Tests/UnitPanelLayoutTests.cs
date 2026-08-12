using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class UnitPanelLayoutTests
{
    [Fact]
    public void CreatesScaleRelativeRegionsFromPrioritySellAndDps()
    {
        UnitPanelLayout? layout = UnitPanelLayout.TryCreate(
            [Region("Priority", 73, 427, 50, 17), Region("Sell", 188, 428, 29, 14), Region("DPS 0/s", 50, 344, 60, 18)],
            new PixelSize(1366, 700));

        Assert.NotNull(layout);
        Assert.Equal(new PixelRect(239, 414, 183, 43), layout.UpgradeControl);
        Assert.Equal(new PixelRect(369, 414, 53, 43), layout.UpgradeExtension);
        Assert.True(UnitPanelLayout.IsPhysicalDps("DPS 0/s"));
        Assert.True(UnitPanelLayout.IsPhysicalDps("DPS Q/s"));
        Assert.False(UnitPanelLayout.IsPhysicalDps("DPS ???"));
        Assert.True(UnitPanelLayout.IsPhantomDps("DPS ???"));
    }

    [Fact]
    public void TrackerRequiresThreeConsistentObservations()
    {
        UnitPanelLayout layout = UnitPanelLayout.TryCreate(
            [Region("Prlorlty", 73, 427, 50, 17), Region("Sell", 188, 428, 29, 14), Region("DPS 1/s", 50, 344, 60, 18)],
            new PixelSize(1366, 700))!;
        UnitPanelLayoutTracker tracker = new();

        Assert.Null(tracker.Observe(layout));
        Assert.Null(tracker.Observe(layout));
        Assert.Same(layout, tracker.Observe(layout));
    }

    [Theory]
    [InlineData(PlacementStepKind.Place, true, false)]
    [InlineData(PlacementStepKind.Reconfigure, true, false)]
    [InlineData(PlacementStepKind.Sell, true, false)]
    [InlineData(PlacementStepKind.Upgrade, false, true)]
    [InlineData(PlacementStepKind.Delay, false, false)]
    [InlineData(PlacementStepKind.StartGame, false, false)]
    public void PhantomPolicyMatchesSupportedUnitPanelActions(
        PlacementStepKind kind,
        bool allowsPhantom,
        bool requiresPhysical)
    {
        Assert.Equal(allowsPhantom, UnitPanelSelectionPolicy.AllowsPhantom(kind));
        Assert.Equal(requiresPhysical, UnitPanelSelectionPolicy.RequiresPhysical(kind));
    }

    [Theory]
    [InlineData(0.66, 0.08, 0.48, UnitUpgradeState.Affordable)]
    [InlineData(0.00, 0.74, 0.48, UnitUpgradeState.Unaffordable)]
    [InlineData(0.00, 0.76, 0.94, UnitUpgradeState.Maxed)]
    public void UpgradeClassifierSeparatesDatasetStates(
        double greenFraction,
        double mainGrayFraction,
        double extensionGrayFraction,
        UnitUpgradeState expected)
    {
        RgbImage main = Synthetic(100, greenFraction, mainGrayFraction);
        RgbImage extension = Synthetic(100, 0, extensionGrayFraction);

        UnitUpgradeObservation result = UnitPanelColorClassifier.ClassifyUpgrade(main, extension);

        Assert.Equal(expected, result.State);
    }

    private static OcrTextRegion Region(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Bounds = new PixelRect(x, y, width, height),
        RecognitionConfidence = 1,
    };

    private static RgbImage Synthetic(int count, double greenFraction, double grayFraction)
    {
        byte[] pixels = new byte[count * 3];
        int green = (int)Math.Round(count * greenFraction);
        int gray = (int)Math.Round(count * grayFraction);
        for (int index = 0; index < count; index++)
        {
            (byte red, byte channelGreen, byte blue) = index < green
                ? ((byte)20, (byte)110, (byte)20)
                : index < green + gray
                    ? ((byte)55, (byte)55, (byte)55)
                    : ((byte)160, (byte)80, (byte)130);
            int offset = index * 3;
            pixels[offset] = red;
            pixels[offset + 1] = channelGreen;
            pixels[offset + 2] = blue;
        }
        return new RgbImage(count, 1, pixels, takeOwnership: true);
    }
}
