using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows;

public readonly record struct ClientBounds(int X, int Y, int Width, int Height)
{
    public PixelSize Size => new(Width, Height);
}
