using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Placements;
using LilacMacro.Core.Datasets;

namespace LilacMacro.Tests;

public sealed class UnitPanelDpsEvidenceTests
{
    [Fact]
    public void CapturePlanUsesPanelScaleInsteadOfFixedPixelPadding()
    {
        UnitPanelLayout small = CreateLayout(73, 188, 50, 17, 29, 14, 50, 344, 60, 18);
        UnitPanelLayout large = CreateLayout(73, 270, 50, 17, 29, 14, 50, 344, 60, 18);

        UnitPanelDpsCapturePlan smallPlan = UnitPanelDpsCapturePlan.Create(
            small, new PixelSize(1366, 700));
        UnitPanelDpsCapturePlan largePlan = UnitPanelDpsCapturePlan.Create(
            large, new PixelSize(1366, 700));

        Assert.True(largePlan.Region.Width > smallPlan.Region.Width);
        Assert.True(largePlan.Region.Height > smallPlan.Region.Height);
        Assert.True(smallPlan.TextBand.IsInside(new PixelSize(
            smallPlan.Region.Width, smallPlan.Region.Height)));
        Assert.True(smallPlan.CoreBand.IsInside(new PixelSize(
            smallPlan.Region.Width, smallPlan.Region.Height)));
    }

    [Fact]
    public void ExactPhantomMatchIgnoresUnstableBackdropAndRejectsPhysicalText()
    {
        UnitPanelDpsCapturePlan plan = new(
            new PixelRect(100, 200, 12, 6),
            new PixelRect(3, 2, 6, 2),
            new PixelRect(1, 1, 10, 4));
        UnitPanelDpsFingerprintBuilder builder = new(plan, new PixelSize(12, 6));
        RgbImage phantom = DpsImage(phantomGlyph: true, backdrop: 30);
        RgbImage phantomWithChangedBackdrop = DpsImage(phantomGlyph: true, backdrop: 80);
        RgbImage physical = DpsImage(phantomGlyph: false, backdrop: 30);

        builder.AddSample(UnitPanelDpsKind.Phantom, phantom);
        builder.AddSample(UnitPanelDpsKind.Phantom, phantomWithChangedBackdrop);
        builder.AddSample(UnitPanelDpsKind.Physical, physical);
        builder.AddSample(UnitPanelDpsKind.Physical, physical);

        UnitPanelDpsFingerprint fingerprint = Assert.IsType<UnitPanelDpsFingerprint>(builder.Fingerprint);
        UnitPanelDpsImageMatch phantomMatch = fingerprint.Match(
            DpsImage(phantomGlyph: true, backdrop: 120));
        UnitPanelDpsImageMatch physicalMatch = fingerprint.Match(physical);

        Assert.True(phantomMatch.IsExact);
        Assert.Equal(1.0, phantomMatch.ExactFraction);
        Assert.False(physicalMatch.IsExact);
        Assert.True(physicalMatch.ExactFraction < 1.0);
        Assert.True(fingerprint.ComparedPixels >= 3);
    }

    [Fact]
    public void FingerprintWaitsForTwoPhantomSamplesAndRejectsWrongSize()
    {
        UnitPanelDpsCapturePlan plan = new(
            new PixelRect(0, 0, 8, 4),
            new PixelRect(2, 1, 4, 2),
            new PixelRect(1, 0, 6, 4));
        UnitPanelDpsFingerprintBuilder builder = new(plan, new PixelSize(8, 4));
        RgbImage phantom = DpsImage(phantomGlyph: true, backdrop: 30, width: 8, height: 4);

        builder.AddSample(UnitPanelDpsKind.Phantom, phantom);
        Assert.Null(builder.Fingerprint);

        builder.AddSample(UnitPanelDpsKind.Phantom, phantom);
        UnitPanelDpsFingerprint fingerprint = Assert.IsType<UnitPanelDpsFingerprint>(builder.Fingerprint);
        Assert.False(fingerprint.Match(new RgbImage(7, 4, new byte[7 * 4 * 3])).IsExact);
    }

    private static UnitPanelLayout CreateLayout(
        int priorityX,
        int sellX,
        int priorityWidth,
        int priorityHeight,
        int sellWidth,
        int sellHeight,
        int dpsX,
        int dpsY,
        int dpsWidth,
        int dpsHeight) => UnitPanelLayout.TryCreate(
            [
                Region("Priority", priorityX, 427, priorityWidth, priorityHeight),
                Region("Sell", sellX, 428, sellWidth, sellHeight),
                Region("DPS 0/s", dpsX, dpsY, dpsWidth, dpsHeight),
            ],
            new PixelSize(1366, 700))!;

    private static RgbImage DpsImage(
        bool phantomGlyph,
        byte backdrop,
        int width = 12,
        int height = 6)
    {
        byte[] pixels = new byte[width * height * 3];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            pixels[pixel * 3] = backdrop;
            pixels[pixel * 3 + 1] = backdrop;
            pixels[pixel * 3 + 2] = backdrop;
        }

        if (width >= 9 && height >= 4)
        {
            for (int x = 3; x < 9; x++)
            {
                SetPixel(pixels, width, x, 2, phantomGlyph ? (byte)240 : (byte)100);
                SetPixel(pixels, width, x, 3, phantomGlyph ? (byte)240 : (byte)100);
            }
        }
        return new RgbImage(width, height, pixels, takeOwnership: true);
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, byte value)
    {
        int offset = (y * width + x) * 3;
        pixels[offset] = value;
        pixels[offset + 1] = value;
        pixels[offset + 2] = value;
    }

    private static OcrTextRegion Region(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Bounds = new PixelRect(x, y, width, height),
        RecognitionConfidence = 1,
    };
}
