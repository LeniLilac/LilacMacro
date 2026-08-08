using LilacMacro.Core.Imaging;

namespace LilacMacro.Core.Vision;

public static class RgbGrayConverter
{
    public static GrayImage Convert(RgbImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] gray = new byte[checked(source.Size.Width * source.Size.Height)];
        for (int pixel = 0, sourceIndex = 0; pixel < gray.Length; pixel++, sourceIndex += 3)
        {
            int luminance =
                54 * source.Pixels[sourceIndex] +
                183 * source.Pixels[sourceIndex + 1] +
                19 * source.Pixels[sourceIndex + 2];
            gray[pixel] = (byte)((luminance + 128) >> 8);
        }
        return new GrayImage(source.Size.Width, source.Size.Height, gray);
    }
}
