using System.Text;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Tests;

public sealed class PngEncoderTests
{
    [Fact]
    public void Encode_DeclaresSrgbColorSpace()
    {
        byte[] png = PngEncoder.Encode(new RgbImage(1, 1, [12, 34, 56]));

        Assert.Contains("sRGB", Encoding.Latin1.GetString(png), StringComparison.Ordinal);
    }
}
