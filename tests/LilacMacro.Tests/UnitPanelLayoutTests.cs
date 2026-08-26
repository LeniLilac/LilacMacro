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
        Assert.Equal(new PixelRect(246, 423, 260, 63), layout.UpgradeControl);
        Assert.Equal(new PixelRect(254, 455, 47, 19), layout.UpgradeFillPrimary);
        Assert.Equal(new PixelRect(408, 435, 24, 36), layout.UpgradeFillSecondary);
        Assert.Equal(new PixelRect(436, 434, 68, 47), layout.UpgradeMaxedReference);
        Assert.True(UnitPanelLayout.IsPhysicalDps("DPS 0/s"));
        Assert.True(UnitPanelLayout.IsPhysicalDps("DPS Q/s"));
        Assert.True(UnitPanelLayout.IsPhysicalDps("DPS 3,786"));
        Assert.True(UnitPanelLayout.IsPhysicalDps("DPS 1,262/"));
        Assert.False(UnitPanelLayout.IsPhysicalDps("DPS ???"));
        Assert.True(UnitPanelLayout.IsPhantomDps("DPS ???"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("DPS")]
    [InlineData("DPS ? ? ?")]
    [InlineData("Damage 3,786/s")]
    public void PhysicalDpsRequiresNonPhantomDpsEvidence(string text) =>
        Assert.False(UnitPanelLayout.IsPhysicalDps(text));

    [Fact]
    public void LayoutCalibrationNeedsDpsGeometryButNotPhysicalClassification()
    {
        UnitPanelLayout? layout = UnitPanelLayout.TryCreate(
            [Region("Priority", 73, 427, 50, 17), Region("Sell", 188, 428, 29, 14), Region("DPS", 50, 344, 60, 18)],
            new PixelSize(1366, 700));

        Assert.NotNull(layout);
        Assert.False(UnitPanelLayout.IsPhysicalDps("DPS"));
        Assert.False(UnitPanelLayout.IsPhantomDps("DPS"));
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
    public void OnlyUpgradeRequiresPhysicalDpsSelectionEvidence(
        PlacementStepKind kind,
        bool allowsPhantom,
        bool requiresPhysicalDpsEvidence)
    {
        Assert.Equal(allowsPhantom, UnitPanelSelectionPolicy.AllowsPhantom(kind));
        Assert.Equal(
            requiresPhysicalDpsEvidence,
            UnitPanelSelectionPolicy.RequiresPhysicalDpsEvidence(kind));
    }

    [Theory]
    [InlineData(0.90, 0.00, 0.88, 0.00, UnitUpgradeState.Affordable)]
    [InlineData(0.00, 0.92, 0.00, 0.90, UnitUpgradeState.Unaffordable)]
    [InlineData(0.90, 0.00, 0.00, 0.90, UnitUpgradeState.Unknown)]
    [InlineData(0.00, 0.69, 0.00, 0.90, UnitUpgradeState.Unknown)]
    public void UpgradeClassifierRequiresTwoIndependentFillRegions(
        double primaryGreenFraction,
        double primaryGrayFraction,
        double secondaryGreenFraction,
        double secondaryGrayFraction,
        UnitUpgradeState expected)
    {
        RgbImage primary = Synthetic(100, primaryGreenFraction, primaryGrayFraction);
        RgbImage secondary = Synthetic(100, secondaryGreenFraction, secondaryGrayFraction);

        UnitUpgradeObservation result = UnitPanelColorClassifier.ClassifyUpgrade(primary, secondary);

        Assert.Equal(expected, result.State);
        Assert.NotEqual(UnitUpgradeState.Maxed, result.State);
    }

    [Theory]
    [InlineData("Maxed", true)]
    [InlineData("Upgrade 3/3 MAXED", true)]
    [InlineData("Upgrade 0/3 ¥1,100", false)]
    [InlineData("", false)]
    public void MaxedOcrRequiresExplicitMaxedText(string text, bool expected) =>
        Assert.Equal(expected, UnitPanelColorClassifier.IsMaxedText(text));

    [Fact]
    public void ConfirmedMaxedReferenceRequiresStrongSameSizeImageMatch()
    {
        RgbImage reference = PanelControl(100, 0, 0, 95, 5);
        RgbImage close = Alter(reference, 5, (70, 70, 70));
        RgbImage different = Alter(reference, 60, (180, 20, 20));
        RgbImage wrongSize = PanelControl(99, 0, 0, 94, 5);

        Assert.True(UnitPanelColorClassifier.MatchConfirmedMaxed(reference, close));
        Assert.False(UnitPanelColorClassifier.MatchConfirmedMaxed(reference, different));
        Assert.False(UnitPanelColorClassifier.MatchConfirmedMaxed(reference, wrongSize));
    }

    [Fact]
    public void UpgradeAttemptScheduleChecksEveryPressAfterConfiguredSettleDelay()
    {
        IReadOnlyList<UnitUpgradeAttempt> attempts = UnitUpgradeAttemptSchedule.Create(6, 200);

        Assert.Equal(Enumerable.Range(1, 6), attempts.Select(attempt => attempt.Number));
        Assert.Equal([0, 200, 200, 200, 200, 200],
            attempts.Select(attempt => attempt.DelayBeforeMilliseconds));
    }

    [Fact]
    public void SelectedPanelImageMatchRejectsColorSimilarMapBackground()
    {
        RgbImage priorityReference = PanelControl(100, 31, 0, 44, 5);
        RgbImage sellReference = PanelControl(100, 14, 18, 51, 4);
        RgbImage priorityPanel = Alter(priorityReference, 8, (40, 65, 150));
        RgbImage sellPanel = Alter(sellReference, 8, (155, 45, 50));
        RgbImage priorityBackground = PanelControl(100, 27, 43, 0, 1);
        RgbImage sellBackground = PanelControl(100, 35, 37, 0, 0);

        Assert.True(UnitPanelColorClassifier.MatchSelectedPanel(
            priorityReference, sellReference, priorityPanel, sellPanel).IsMatch);
        UnitPanelImageMatch background = UnitPanelColorClassifier.MatchSelectedPanel(
            priorityReference, sellReference, priorityBackground, sellBackground);

        Assert.False(background.IsMatch);
        Assert.True(background.PrioritySimilarity < UnitPanelColorClassifier.MinimumReferenceSimilarity);
        Assert.True(background.SellSimilarity < UnitPanelColorClassifier.MinimumReferenceSimilarity);
    }

    [Fact]
    public void SelectedPanelRetainsOwnershipWhenMutableControlContentChanges()
    {
        RgbImage priorityReference = PanelControl(100, 31, 0, 44, 25);
        RgbImage sellReference = PanelControl(100, 0, 24, 51, 25);
        RgbImage changedPriority = PanelControl(100, 20, 0, 0, 80);
        RgbImage changedSell = PanelControl(100, 0, 18, 0, 82);

        UnitPanelImageMatch match = UnitPanelColorClassifier.MatchSelectedPanel(
            priorityReference,
            sellReference,
            changedPriority,
            changedSell);

        Assert.True(match.IsMatch);
        Assert.True(match.PrioritySimilarity < UnitPanelColorClassifier.MinimumReferenceSimilarity);
        Assert.True(match.SellSimilarity < UnitPanelColorClassifier.MinimumReferenceSimilarity);
    }

    [Fact]
    public void SelectedPanelToleratesBoundedPriorityBlueOverlapInSellCrop()
    {
        RgbImage priorityReference = PanelControl(1000, 310, 0, 440, 50);
        RgbImage sellReference = PanelControl(1000, 140, 180, 510, 40);
        RgbImage priorityPanel = PanelControl(1000, 287, 0, 500, 50);
        RgbImage sellPanel = PanelControl(1000, 145, 181, 500, 40);

        UnitPanelImageMatch match = UnitPanelColorClassifier.MatchSelectedPanel(
            priorityReference,
            sellReference,
            priorityPanel,
            sellPanel);

        Assert.True(match.IsMatch);
        Assert.InRange(match.SellBlueFraction, 0.14, 0.15);
        Assert.InRange(match.SellRedFraction, 0.18, 0.19);
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

    private static RgbImage PanelControl(int count, int blue, int red, int dark, int white)
    {
        byte[] pixels = new byte[count * 3];
        for (int index = 0; index < count; index++)
        {
            (byte redChannel, byte greenChannel, byte blueChannel) = index < blue
                ? ((byte)45, (byte)75, (byte)160)
                : index < blue + red
                    ? ((byte)175, (byte)50, (byte)55)
                    : index < blue + red + dark
                        ? ((byte)20, (byte)20, (byte)20)
                        : index < blue + red + dark + white
                            ? ((byte)230, (byte)230, (byte)230)
                            : ((byte)115, (byte)85, (byte)125);
            int offset = index * 3;
            pixels[offset] = redChannel;
            pixels[offset + 1] = greenChannel;
            pixels[offset + 2] = blueChannel;
        }
        return new RgbImage(count, 1, pixels, takeOwnership: true);
    }

    private static RgbImage Alter(RgbImage source, int pixels, (byte Red, byte Green, byte Blue) color)
    {
        byte[] changed = source.Pixels.ToArray();
        for (int index = 0; index < pixels; index++)
        {
            changed[index * 3] = color.Red;
            changed[index * 3 + 1] = color.Green;
            changed[index * 3 + 2] = color.Blue;
        }
        return new RgbImage(source.Size.Width, source.Size.Height, changed, takeOwnership: true);
    }
}
