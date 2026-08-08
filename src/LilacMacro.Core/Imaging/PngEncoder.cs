using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace LilacMacro.Core.Imaging;

public static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(RgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using MemoryStream output = new();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)image.Size.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)image.Size.Height));
        header[8] = 8;
        header[9] = 2;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "sRGB", [0]);

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            int stride = checked(image.Size.Width * 3);
            for (int row = 0; row < image.Size.Height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Pixels, row * stride, stride);
            }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream destination, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        destination.Write(length);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        destination.Write(typeBytes);
        destination.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte value in typeBytes) crc = UpdateCrc(crc, value);
        foreach (byte value in data) crc = UpdateCrc(crc, value);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc ^ 0xFFFFFFFF);
        destination.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, byte value) => CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            uint crc = value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
            table[value] = crc;
        }
        return table;
    }
}
