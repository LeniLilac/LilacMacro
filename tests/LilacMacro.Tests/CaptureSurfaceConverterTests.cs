using LilacMacro.Core.Imaging;
using LilacMacro.Windows.Capture;
using Vortice.DXGI;

namespace LilacMacro.Tests;

public sealed class CaptureSurfaceConverterTests
{
    [Fact]
    public void SdrWhiteBoost_IsRemovedBeforeSrgbEncoding()
    {
        RgbImage ordinary = ConvertPixel(0.5f, 0.5f, 0.5f, CaptureColorContext.StandardSdr);
        RgbImage boosted = ConvertPixel(1f, 1f, 1f, new CaptureColorContext(true, 160f, 1000f));

        Assert.Equal(ordinary.Pixels, boosted.Pixels);
        Assert.Equal([188, 188, 188], boosted.Pixels);
    }

    [Fact]
    public void HdrHighlight_IsCompressedWithoutClippingEveryChannel()
    {
        CaptureColorContext context = new(true, 80f, 1000f);

        RgbImage image = ConvertPixel(4f, 1f, 0.2f, context);

        Assert.Equal(255, image.Pixels[0]);
        Assert.InRange(image.Pixels[1], 1, 254);
        Assert.InRange(image.Pixels[2], 0, image.Pixels[1] - 1);
    }

    [Fact]
    public void ExtendedGamut_IsCompressedTowardLuminanceInsteadOfScalingByMaximum()
    {
        RgbImage image = ConvertPixel(1.2f, 0.4f, 0.2f, CaptureColorContext.StandardSdr);

        Assert.Equal(255, image.Pixels[0]);
        Assert.True(image.Pixels[1] > 0);
        Assert.True(image.Pixels[2] > 0);
        Assert.True(image.Pixels[0] > image.Pixels[1]);
        Assert.True(image.Pixels[1] > image.Pixels[2]);
    }

    [Fact]
    public void NegativeScRgbPrimary_IsGamutCompressedWithoutDiscardingOtherChannels()
    {
        RgbImage image = ConvertPixel(-0.1f, 0.5f, 0.5f, CaptureColorContext.StandardSdr);

        Assert.Equal(0, image.Pixels[0]);
        Assert.True(image.Pixels[1] > image.Pixels[0]);
        Assert.Equal(image.Pixels[1], image.Pixels[2]);
    }

    [Fact]
    public void InvalidDisplayMeasurements_UseBoundedFallbacks()
    {
        CaptureColorContext context = new(true, float.NaN, -1f);

        Assert.Equal(80f, context.SdrWhiteLevelNits);
        Assert.Equal(1000f, context.DisplayMaxLuminanceNits);
        Assert.Equal(1f, context.ScRgbReferenceScale);
        Assert.Equal(12.5f, context.RelativeDisplayPeak);
    }

    [Fact]
    public void DisplayConfigInterop_UsesWindowsSdkStructureSizes()
    {
        Assert.Equal([20, 84, 24, 72, 64], DisplayConfigQuery.InteropLayoutSizes);
    }

    [Fact]
    public void ElevatedSdrWhiteActivatesReferenceWhiteCorrectionEvenWhenOutputReportsSdr()
    {
        CaptureColorContext context = DisplayColorContextProvider.FromOutputObservation(
            ColorSpaceType.RgbFullG22NoneP709,
            160f,
            1000f);

        Assert.True(context.AdvancedColorActive);
        Assert.Equal(0.5f, context.ScRgbReferenceScale);
        Assert.Equal("elevated-sdr-white", context.Detection);
        Assert.False(context.UsedSdrWhiteFallback);
    }

    [Fact]
    public void AdvancedOutputWithoutWhiteMeasurementUsesWindowsReferenceFallback()
    {
        CaptureColorContext context = DisplayColorContextProvider.FromOutputObservation(
            ColorSpaceType.RgbFullG2084NoneP2020,
            null,
            1000f);

        Assert.True(context.AdvancedColorActive);
        Assert.Equal(CaptureColorContext.AdvancedColorFallbackWhiteNits, context.SdrWhiteLevelNits);
        Assert.True(context.UsedSdrWhiteFallback);
        Assert.Equal("advanced-color-space+fallback-white", context.Detection);
    }

    [Fact]
    public void SdrOutputWithReferenceWhiteRemainsUnscaled()
    {
        CaptureColorContext context = DisplayColorContextProvider.FromOutputObservation(
            ColorSpaceType.RgbFullG22NoneP709,
            80f,
            80f);

        Assert.False(context.AdvancedColorActive);
        Assert.Equal(1f, context.ScRgbReferenceScale);
        Assert.Equal("sdr-color-space", context.Detection);
    }

    [Fact]
    public void FrameDiagnosticsSummarizeBrightnessWithoutRetainingPixels()
    {
        RgbImage image = new(2, 2,
        [
            255, 255, 255,
            255, 0, 0,
            0, 0, 0,
            128, 128, 128,
        ], takeOwnership: true);

        CaptureFrameDiagnostics diagnostics = RobloxCaptureService.AnalyzeFrame(image);

        Assert.Equal(25, diagnostics.NearWhitePixelPercent);
        Assert.Equal(50, diagnostics.ClippedPixelPercent);
        Assert.Equal(25, diagnostics.DarkPixelPercent);
        Assert.Equal(1, diagnostics.P95SrgbLuminance);
        Assert.InRange(diagnostics.MeanSrgbLuminance, 0.42, 0.43);
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(false, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 1, false)]
    public void CaptureSessionRecoveryIsBoundedToOneRetry(bool disposed, int attempt, bool expected)
    {
        Exception error = disposed
            ? new ObjectDisposedException("capture")
            : new TimeoutException("capture");

        Assert.Equal(expected, WindowsGraphicsCapture.ShouldRebuildCaptureSession(error, attempt));
        Assert.False(WindowsGraphicsCapture.ShouldRebuildCaptureSession(
            new InvalidOperationException("unrelated"),
            attempt));
    }

    private static RgbImage ConvertPixel(
        float red,
        float green,
        float blue,
        CaptureColorContext context)
    {
        byte[] source = new byte[8];
        WriteHalf(source, 0, red);
        WriteHalf(source, 2, green);
        WriteHalf(source, 4, blue);
        WriteHalf(source, 6, 1f);
        return CaptureSurfaceConverter.ConvertScRgbRgba16ToRgb(
            source,
            1,
            1,
            new ScreenRegion(0, 0, 1, 1),
            context);
    }

    private static void WriteHalf(byte[] destination, int offset, float value)
    {
        ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
        destination[offset] = (byte)bits;
        destination[offset + 1] = (byte)(bits >> 8);
    }
}
