using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Vision;
using LilacMacro.Windows.Capture;

namespace LilacMacro.Tests;

public sealed class CaptureAtlasLayoutTests
{
    [Fact]
    public void Create_PacksRegionsIntoCompactRowsAndPreservesRequestOrder()
    {
        PixelRect[] regions =
        [
            new(10, 10, 30, 20),
            new(50, 10, 40, 10),
            new(0, 40, 10, 30),
        ];

        CaptureAtlasLayout layout = CaptureAtlasLayout.Create(100, 80, regions);

        Assert.Equal(40, layout.Width);
        Assert.Equal(40, layout.Height);
        Assert.Equal(new ScreenRegion(10, 0, 30, 20), layout.Entries[0].Atlas);
        Assert.Equal(new ScreenRegion(0, 30, 40, 10), layout.Entries[1].Atlas);
        Assert.Equal(new ScreenRegion(0, 0, 10, 30), layout.Entries[2].Atlas);
    }

    [Fact]
    public void Create_RejectsRegionOutsideClient()
    {
        PixelRect[] regions = [new(90, 70, 11, 10)];

        Assert.Throws<ArgumentOutOfRangeException>(() => CaptureAtlasLayout.Create(100, 80, regions));
    }

    [Fact]
    public void Create_RejectsRequestsLargerThanOneClientReadback()
    {
        PixelRect[] regions = [new(0, 0, 100, 80), new(0, 0, 1, 1)];

        Assert.Throws<ArgumentOutOfRangeException>(() => CaptureAtlasLayout.Create(100, 80, regions));
    }

    [Fact]
    public void RgbGrayConverter_UsesDeterministicRec709Weights()
    {
        RgbImage source = new(3, 1, [255, 0, 0, 0, 255, 0, 0, 0, 255]);

        GrayImage gray = RgbGrayConverter.Convert(source);

        Assert.Equal(new byte[] { 54, 182, 19 }, gray.Pixels.ToArray());
    }
}
