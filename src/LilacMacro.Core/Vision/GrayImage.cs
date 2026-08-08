namespace LilacMacro.Core.Vision;

public sealed class GrayImage
{
    private readonly byte[] _pixels;

    public GrayImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixels.Length != checked(width * height))
        {
            throw new ArgumentException("Pixel count must equal width times height.", nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Pixels => _pixels;

    public byte this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
            return _pixels[checked(y * Width + x)];
        }
    }

    internal ReadOnlySpan<byte> PixelSpan => _pixels;
}
