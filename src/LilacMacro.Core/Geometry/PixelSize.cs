namespace LilacMacro.Core.Geometry;

public readonly record struct PixelSize(int Width, int Height)
{
    public static PixelSize Create(int width, int height)
    {
        if (width is < 320 or > 7680)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Client width must be between 320 and 7680 pixels.");
        }
        if (height is < 240 or > 4320)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Client height must be between 240 and 4320 pixels.");
        }

        return new PixelSize(width, height);
    }

    public override string ToString() => $"{Width} × {Height}";
}
