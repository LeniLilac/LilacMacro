using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Imaging;

public sealed class RgbImage
{
    public RgbImage(int width, int height, byte[] pixels, bool takeOwnership = false)
    {
        Size = new PixelSize(width, height);
        int expected = checked(width * height * 3);
        if (pixels.Length != expected)
        {
            throw new ArgumentException($"RGB buffer has {pixels.Length} bytes; expected {expected}.", nameof(pixels));
        }
        Pixels = takeOwnership ? pixels : [.. pixels];
    }

    public PixelSize Size { get; }

    public byte[] Pixels { get; }
}
