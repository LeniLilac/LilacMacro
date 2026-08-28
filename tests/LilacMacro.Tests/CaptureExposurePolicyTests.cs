using LilacMacro.Core.Imaging;
using LilacMacro.Windows.Capture;

namespace LilacMacro.Tests;

public sealed class CaptureExposurePolicyTests
{
    [Fact]
    public void PersistentWashedOutAutoHdrShape_TriggersBoundedProbe()
    {
        CaptureFrameDiagnostics frame = new(0.50, 0.96, 6.1, 24.3, 2.9);
        CaptureFrameDiagnostics[] observations = Enumerable
            .Repeat(frame, CaptureExposurePolicy.ObservationWindowSize)
            .ToArray();

        Assert.True(CaptureExposurePolicy.IsEligibleObservation(frame));
        Assert.True(CaptureExposurePolicy.IsSuspiciousWindow(observations));
        Assert.Equal(24, CaptureExposurePolicy.ObservationWindowSize);
        Assert.Equal(2, CaptureExposurePolicy.MaximumProbeAttempts);
    }

    [Theory]
    [InlineData(0.20, 0.45, 0.0, 0.7)]
    [InlineData(1.00, 1.00, 100.0, 100.0)]
    [InlineData(0.50, 0.96, 20.0, 24.3)]
    [InlineData(0.05, 0.96, 6.0, 24.3)]
    public void OrdinaryOrTransitionFrames_DoNotTriggerProbe(
        double mean,
        double p95,
        double nearWhite,
        double clipped)
    {
        CaptureFrameDiagnostics frame = new(mean, p95, nearWhite, clipped, 1.0);
        CaptureFrameDiagnostics[] observations = Enumerable
            .Repeat(frame, CaptureExposurePolicy.ObservationWindowSize)
            .ToArray();

        Assert.False(
            CaptureExposurePolicy.IsEligibleObservation(frame) &&
            CaptureExposurePolicy.IsSuspiciousWindow(observations));
    }

    [Fact]
    public void ShortOrMostlyOrdinaryWindow_DoesNotTriggerProbe()
    {
        CaptureFrameDiagnostics washed = new(0.50, 0.96, 6.1, 24.3, 2.9);
        CaptureFrameDiagnostics ordinary = new(0.20, 0.45, 0.1, 0.7, 2.0);
        CaptureFrameDiagnostics[] shortWindow = Enumerable.Repeat(washed, 23).ToArray();
        CaptureFrameDiagnostics[] mixedWindow =
        [
            .. Enumerable.Repeat(washed, 11),
            .. Enumerable.Repeat(ordinary, 13),
        ];

        Assert.False(CaptureExposurePolicy.IsSuspiciousWindow(shortWindow));
        Assert.False(CaptureExposurePolicy.IsSuspiciousWindow(mixedWindow));
    }

    [Fact]
    public void CorrelatedLowerExposureCandidate_IsAccepted()
    {
        CaptureFrameDiagnostics source = new(0.50, 0.96, 6.1, 24.3, 2.9);
        CaptureFrameDiagnostics candidate = new(0.24, 0.72, 0.4, 1.2, 8.0);

        Assert.True(CaptureExposurePolicy.ShouldAcceptFallback(source, candidate, 0.82));
    }

    [Theory]
    [InlineData(0.03, 0.30, 0.0, 0.0, 0.82)]
    [InlineData(0.24, 0.72, 0.4, 1.2, 0.40)]
    [InlineData(0.48, 0.94, 5.0, 20.0, 0.82)]
    public void DarkUnrelatedOrUncorrectedCandidate_IsRejected(
        double mean,
        double p95,
        double nearWhite,
        double clipped,
        double correlation)
    {
        CaptureFrameDiagnostics source = new(0.50, 0.96, 6.1, 24.3, 2.9);
        CaptureFrameDiagnostics candidate = new(mean, p95, nearWhite, clipped, 8.0);

        Assert.False(CaptureExposurePolicy.ShouldAcceptFallback(source, candidate, correlation));
    }

    [Fact]
    public void StructuralCorrelation_ToleratesExposureChangeButRejectsDifferentLayout()
    {
        RgbImage source = CreateGradient(reverse: false, scale: 1.0);
        RgbImage corrected = CreateGradient(reverse: false, scale: 0.55);
        RgbImage unrelated = CreateGradient(reverse: true, scale: 1.0);

        Assert.True(CaptureExposurePolicy.MeasureStructuralCorrelation(source, corrected) > 0.99);
        Assert.True(CaptureExposurePolicy.MeasureStructuralCorrelation(source, unrelated) < -0.90);
    }

    [Fact]
    public void ExposureFallbackContext_UsesExplicitAutoHdrReferenceAndDiagnostics()
    {
        CaptureColorDiagnostics source = CaptureColorContext.StandardSdr.ToDiagnostics();

        CaptureColorContext fallback = WindowsGraphicsCapture.CreateExposureFallbackContext(source);

        Assert.True(fallback.AdvancedColorActive);
        Assert.Equal(CaptureColorContext.AdvancedColorFallbackWhiteNits, fallback.SdrWhiteLevelNits);
        Assert.Equal(1000f, fallback.DisplayMaxLuminanceNits);
        Assert.True(fallback.UsedSdrWhiteFallback);
        Assert.Equal("bgra8-exposure-anomaly+forced-scrgb", fallback.Detection);
    }

    private static RgbImage CreateGradient(bool reverse, double scale)
    {
        byte[] pixels = new byte[16 * 16 * 3];
        for (int pixel = 0; pixel < pixels.Length / 3; pixel++)
        {
            int source = reverse ? 255 - pixel : pixel;
            byte value = (byte)Math.Clamp((int)Math.Round(source * scale), 0, 255);
            pixels[pixel * 3] = value;
            pixels[pixel * 3 + 1] = value;
            pixels[pixel * 3 + 2] = value;
        }
        return new RgbImage(16, 16, pixels, takeOwnership: true);
    }
}
