using LilacMacro.Core.Geometry;
using LilacMacro.Core.Vision;

namespace LilacMacro.Tests;

public sealed class VisualAnchorRegionMatcherTests
{
    [Fact]
    public void GetCaptureBounds_ContainsEveryConfiguredCandidateNearClientEdge()
    {
        VisualAnchorMatcherOptions options = new()
        {
            HorizontalSearchRadius = 16,
            VerticalSearchRadius = 12,
            ScaleFactors = [0.75, 1, 1.25],
        };

        PixelRect capture = VisualAnchorRegionMatcher.GetCaptureBounds(
            new PixelSize(100, 80),
            new PixelRect(2, 3, 20, 12),
            options);

        Assert.Equal(new PixelRect(0, 0, 41, 29), capture);
    }

    [Fact]
    public void Match_ReturnsClientCoordinatesAfterRegionOnlyMatching()
    {
        byte[] pattern = Enumerable.Range(0, 64)
            .Select(index => (byte)(((index / 8 + index % 8) & 1) == 0 ? 32 : 224))
            .ToArray();
        byte[] reliability = Enumerable.Repeat((byte)255, 64).ToArray();
        GrayImage template = new(8, 8, pattern);
        VisualAnchorProfile profile = new(
            new VisualAnchorDefinition("test", ["test"]),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            VisualAnchorStrategy.StableAppearance,
            40,
            40,
            3,
            8,
            8,
            template,
            template,
            new GrayImage(8, 8, reliability),
            new GrayImage(8, 8, reliability),
            [template],
            new VisualFingerprintMetrics(0, 1, 0, 1, 1));
        byte[] fullPixels = new byte[40 * 40];
        for (int y = 0; y < 8; y++)
        {
            pattern.AsSpan(y * 8, 8).CopyTo(fullPixels.AsSpan((17 + y) * 40 + 18, 8));
        }
        GrayImage full = new(40, 40, fullPixels);
        PixelRect expected = new(16, 16, 8, 8);
        VisualAnchorMatcherOptions options = new()
        {
            HorizontalSearchRadius = 4,
            VerticalSearchRadius = 4,
            SearchStep = 1,
            ScaleFactors = [1],
            MinimumScore = 0.4,
            MinimumDistinctMargin = 0,
        };
        PixelRect captureBounds = VisualAnchorRegionMatcher.GetCaptureBounds(new PixelSize(40, 40), expected, options);
        byte[] croppedPixels = new byte[captureBounds.Width * captureBounds.Height];
        for (int y = 0; y < captureBounds.Height; y++)
        {
            full.Pixels.Span.Slice(
                (captureBounds.Y + y) * full.Width + captureBounds.X,
                captureBounds.Width).CopyTo(croppedPixels.AsSpan(y * captureBounds.Width, captureBounds.Width));
        }

        VisualAnchorMatchResult result = new VisualAnchorRegionMatcher().Match(
            new GrayImage(captureBounds.Width, captureBounds.Height, croppedPixels),
            captureBounds,
            profile,
            expected,
            options);

        Assert.Equal(VisualAnchorMatchStatus.Matched, result.Status);
        Assert.Equal(new PixelRect(18, 17, 8, 8), result.Bounds);
    }
}
