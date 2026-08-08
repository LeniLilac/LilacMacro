using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Automation;

public sealed record QuickPlacementPoint(
    int UnitSlot,
    PixelPoint Point)
{
    public QuickPlacementPoint Validate(PixelSize clientSize)
    {
        if (UnitSlot is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(UnitSlot));
        if (Point.X < 0 || Point.Y < 0 || Point.X >= clientSize.Width || Point.Y >= clientSize.Height)
            throw new ArgumentOutOfRangeException(nameof(Point));
        return this;
    }
}
