using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Windows;
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

    [Fact]
    public void BgraCompatibilitySurfaceConvertsAndCropsWithoutColorTransform()
    {
        byte[] bgra =
        [
            30, 20, 10, 255,
            60, 50, 40, 255,
            90, 80, 70, 255,
            120, 110, 100, 255,
        ];

        RgbImage image = CaptureSurfaceConverter.ConvertBgra8ToRgb(
            bgra,
            2,
            2,
            new ScreenRegion(1, 0, 1, 2));

        Assert.Equal(1, image.Size.Width);
        Assert.Equal(2, image.Size.Height);
        Assert.Equal([40, 50, 60, 100, 110, 120], image.Pixels.ToArray());
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(false, 0, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, 2, false)]
    [InlineData(false, 2, false)]
    public void CaptureSessionRecoveryUsesThreeBoundedAttempts(bool disposed, int attempt, bool expected)
    {
        Exception error = disposed
            ? new ObjectDisposedException("capture")
            : new TimeoutException("capture");

        Assert.Equal(expected, WindowsGraphicsCapture.ShouldRetryCapture(error, attempt));
        Assert.False(WindowsGraphicsCapture.ShouldRetryCapture(
            new InvalidOperationException("unrelated"),
            attempt));
    }

    [Fact]
    public void CaptureSessionRecoveryFallsBackAfterSecondScRgbFailure()
    {
        Assert.Equal(
            CaptureSurfaceFormat.ScRgbFloat,
            WindowsGraphicsCapture.SelectRetryFormat(CaptureSurfaceFormat.ScRgbFloat, 0));
        Assert.Equal(
            CaptureSurfaceFormat.Bgra8,
            WindowsGraphicsCapture.SelectRetryFormat(CaptureSurfaceFormat.ScRgbFloat, 1));
        Assert.Equal(
            CaptureSurfaceFormat.Bgra8,
            WindowsGraphicsCapture.SelectRetryFormat(CaptureSurfaceFormat.Bgra8, 0));
    }

    [Fact]
    public void CaptureSessionTargetIncludesRobloxProcessIdentity()
    {
        Assert.True(WindowsGraphicsCapture.IsSameTarget((nint)42, 100, (nint)42, 100));
        Assert.False(WindowsGraphicsCapture.IsSameTarget((nint)42, 100, (nint)42, 101));
        Assert.False(WindowsGraphicsCapture.IsSameTarget((nint)42, 100, (nint)43, 100));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void CaptureSurfaceRecreationIsBounded(int completedAttempts, bool expected)
    {
        Assert.Equal(expected, WindowsGraphicsCapture.ShouldRecreateSurface(completedAttempts));
    }

    [Fact]
    public void CaptureSurfaceRecreationCanRemapClientInsideIncomingWindowSurface()
    {
        ScreenRegion crop = WindowsGraphicsCapture.ResolveClientCrop(
            1382,
            739,
            new ClientBounds(108, 131, 1366, 700),
            new WindowBounds(100, 100, 1382, 739),
            new WindowBounds(100, 100, 1382, 739));

        Assert.Equal(new ScreenRegion(8, 31, 1366, 700), crop);
    }

    [Fact]
    public async Task CaptureFrameArrivalNotificationCrossesThreadsAndIgnoresLateCallbacks()
    {
        FrameArrivalGate gate = new();
        long targetGeneration = gate.Generation + 1;

        Task<bool> wait = Task.Run(() => gate.WaitForGeneration(targetGeneration, 1000));
        await Task.Run(gate.Notify);

        Assert.True(await wait);
        gate.Dispose();
        gate.Notify();
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
