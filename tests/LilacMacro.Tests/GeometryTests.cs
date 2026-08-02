using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void FromDrag_NormalizesAndClampsToImage()
    {
        PixelRect? result = PixelRect.FromDrag(310.2, 250.9, -8.5, 4.2, new PixelSize(320, 240));

        Assert.Equal(new PixelRect(0, 4, 311, 236), result);
    }

    [Fact]
    public void FromDrag_RejectsAccidentalTinyRegions()
    {
        PixelRect? result = PixelRect.FromDrag(10, 10, 11.9, 12, new PixelSize(320, 240));

        Assert.Null(result);
    }
}
