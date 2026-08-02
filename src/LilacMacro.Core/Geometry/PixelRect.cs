using System.Text.Json.Serialization;

namespace LilacMacro.Core.Geometry;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    [JsonIgnore]
    public int Right => checked(X + Width);

    [JsonIgnore]
    public int Bottom => checked(Y + Height);

    public bool IsInside(PixelSize image) =>
        X >= 0 &&
        Y >= 0 &&
        Width > 0 &&
        Height > 0 &&
        Right <= image.Width &&
        Bottom <= image.Height;

    public bool Contains(PixelRect rectangle) =>
        rectangle.X >= X &&
        rectangle.Y >= Y &&
        rectangle.Width > 0 &&
        rectangle.Height > 0 &&
        rectangle.Right <= Right &&
        rectangle.Bottom <= Bottom;

    public static PixelRect? FromDrag(
        double startX,
        double startY,
        double endX,
        double endY,
        PixelSize image,
        int minimumExtent = 3)
    {
        if (minimumExtent < 1) throw new ArgumentOutOfRangeException(nameof(minimumExtent));

        int left = Math.Clamp((int)Math.Floor(Math.Min(startX, endX)), 0, image.Width);
        int top = Math.Clamp((int)Math.Floor(Math.Min(startY, endY)), 0, image.Height);
        int right = Math.Clamp((int)Math.Ceiling(Math.Max(startX, endX)), 0, image.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(Math.Max(startY, endY)), 0, image.Height);
        int width = right - left;
        int height = bottom - top;

        return width < minimumExtent || height < minimumExtent
            ? null
            : new PixelRect(left, top, width, height);
    }
}
