using System.Text;

namespace LilacMacro.Core.Vision;

internal static class PortableGraymap
{
    public static byte[] Encode(GrayImage image)
    {
        byte[] header = Encoding.ASCII.GetBytes($"P5\n{image.Width} {image.Height}\n255\n");
        byte[] output = new byte[checked(header.Length + image.Pixels.Length)];
        header.CopyTo(output, 0);
        image.PixelSpan.CopyTo(output.AsSpan(header.Length));
        return output;
    }

    public static GrayImage Decode(ReadOnlySpan<byte> bytes)
    {
        int cursor = 0;
        string magic = ReadToken(bytes, ref cursor);
        string widthText = ReadToken(bytes, ref cursor);
        string heightText = ReadToken(bytes, ref cursor);
        string maximumText = ReadToken(bytes, ref cursor);
        if (magic != "P5" || maximumText != "255" ||
            !int.TryParse(widthText, out int width) || !int.TryParse(heightText, out int height) ||
            width < 1 || height < 1)
        {
            throw new InvalidDataException("Portable graymap header is invalid.");
        }

        if (cursor >= bytes.Length || !char.IsWhiteSpace((char)bytes[cursor]))
        {
            throw new InvalidDataException("Portable graymap header has no pixel delimiter.");
        }
        cursor++;
        int expected = checked(width * height);
        if (bytes.Length - cursor != expected)
        {
            throw new InvalidDataException("Portable graymap pixel length is invalid.");
        }

        return new GrayImage(width, height, bytes[cursor..]);
    }

    private static string ReadToken(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        while (cursor < bytes.Length && char.IsWhiteSpace((char)bytes[cursor])) cursor++;
        int start = cursor;
        while (cursor < bytes.Length && !char.IsWhiteSpace((char)bytes[cursor])) cursor++;
        if (start == cursor) throw new InvalidDataException("Portable graymap header is incomplete.");
        return Encoding.ASCII.GetString(bytes[start..cursor]);
    }
}
