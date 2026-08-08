using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Vision;

public sealed class VisualAnchorRegionMatcher
{
    private readonly VisualAnchorMatcher _matcher = new();

    public static PixelRect GetCaptureBounds(
        PixelSize clientSize,
        PixelRect expectedBounds,
        VisualAnchorMatcherOptions? options = null)
    {
        options ??= new VisualAnchorMatcherOptions();
        options.Validate();
        if (!expectedBounds.IsInside(clientSize)) throw new ArgumentOutOfRangeException(nameof(expectedBounds));

        int maximumWidth = options.ScaleFactors
            .Select(scale => Math.Max(8, (int)Math.Round(expectedBounds.Width * scale)))
            .Max();
        int maximumHeight = options.ScaleFactors
            .Select(scale => Math.Max(8, (int)Math.Round(expectedBounds.Height * scale)))
            .Max();
        int centerX = expectedBounds.X + expectedBounds.Width / 2;
        int centerY = expectedBounds.Y + expectedBounds.Height / 2;
        int left = Math.Max(0, centerX - options.HorizontalSearchRadius - maximumWidth / 2);
        int top = Math.Max(0, centerY - options.VerticalSearchRadius - maximumHeight / 2);
        int right = Math.Min(
            clientSize.Width,
            centerX + options.HorizontalSearchRadius + maximumWidth - maximumWidth / 2);
        int bottom = Math.Min(
            clientSize.Height,
            centerY + options.VerticalSearchRadius + maximumHeight - maximumHeight / 2);
        return new PixelRect(left, top, checked(right - left), checked(bottom - top));
    }

    public VisualAnchorMatchResult Match(
        GrayImage capturedRegion,
        PixelRect capturedBounds,
        VisualAnchorProfile profile,
        PixelRect expectedClientBounds,
        VisualAnchorMatcherOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(capturedRegion);
        if (capturedRegion.Width != capturedBounds.Width || capturedRegion.Height != capturedBounds.Height)
        {
            throw new ArgumentException("Captured pixels must match their client-relative bounds.", nameof(capturedRegion));
        }
        PixelRect localExpected = new(
            checked(expectedClientBounds.X - capturedBounds.X),
            checked(expectedClientBounds.Y - capturedBounds.Y),
            expectedClientBounds.Width,
            expectedClientBounds.Height);
        VisualAnchorMatchResult result = _matcher.Match(capturedRegion, profile, localExpected, options);
        PixelRect? clientBounds = result.Bounds is PixelRect local
            ? new PixelRect(
                checked(local.X + capturedBounds.X),
                checked(local.Y + capturedBounds.Y),
                local.Width,
                local.Height)
            : null;
        return result with { Bounds = clientBounds };
    }
}
