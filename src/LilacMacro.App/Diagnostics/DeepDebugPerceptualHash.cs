using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace LilacMacro.App.Diagnostics;

internal static class DeepDebugPerceptualHash
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static ulong Create(byte[] png)
    {
        try { return Decode(png); }
        catch (Exception error) when (error is IOException or InvalidDataException or OverflowException)
        {
            return BitConverter.ToUInt64(SHA256.HashData(png));
        }
    }

    public static int Distance(ulong left, ulong right) =>
        System.Numerics.BitOperations.PopCount(left ^ right);

    public static (int Width, int Height, byte[] Digest) CreatePixelDigest(byte[] png)
    {
        DecodedPng decoded = DecodePixels(png);
        return (decoded.Width, decoded.Height, SHA256.HashData(decoded.Pixels));
    }

    private static ulong Decode(ReadOnlySpan<byte> png)
    {
        DecodedPng decoded = DecodePixels(png);
        int width = decoded.Width;
        int height = decoded.Height;
        int channels = decoded.Channels;
        byte[] pixels = decoded.Pixels;
        long[] sums = new long[64];
        int[] counts = new int[64];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int source = (y * width + x) * channels;
                int luminance = channels == 1
                    ? pixels[source]
                    : (pixels[source] * 54 + pixels[source + 1] * 183 + pixels[source + 2] * 19) >> 8;
                int bin = Math.Min(7, y * 8 / height) * 8 + Math.Min(7, x * 8 / width);
                sums[bin] += luminance;
                counts[bin]++;
            }
        }
        double[] averages = sums.Select((sum, index) =>
            counts[index] == 0 ? 0d : (double)sum / counts[index]).ToArray();
        double global = averages.Average();
        ulong hash = 0;
        for (int index = 0; index < averages.Length; index++)
            if (averages[index] >= global) hash |= 1UL << index;
        return hash;
    }

    private static DecodedPng DecodePixels(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG image.");
        int width = 0;
        int height = 0;
        int channels = 0;
        using MemoryStream compressed = new();
        int offset = Signature.Length;
        while (offset <= png.Length - 12)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]));
            if (length < 0 || offset + 12L + length > png.Length)
                throw new InvalidDataException("PNG chunk length is invalid.");
            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);
            ReadOnlySpan<byte> data = png.Slice(offset + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13 || data[8] != 8 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new InvalidDataException("PNG layout is unsupported.");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                channels = data[9] switch { 0 => 1, 2 => 3, 6 => 4, _ => 0 };
            }
            else if (type.SequenceEqual("IDAT"u8)) compressed.Write(data);
            else if (type.SequenceEqual("IEND"u8)) break;
            offset = checked(offset + length + 12);
        }
        if (width <= 0 || height <= 0 || channels == 0 || (long)width * height > 16_000_000)
            throw new InvalidDataException("PNG dimensions or color format are unsupported.");

        int stride = checked(width * channels);
        byte[] previous = new byte[stride];
        byte[] current = new byte[stride];
        byte[] pixels = new byte[checked(stride * height)];
        compressed.Position = 0;
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        for (int y = 0; y < height; y++)
        {
            int filter = zlib.ReadByte();
            if (filter < 0) throw new InvalidDataException("PNG pixel data ended early.");
            zlib.ReadExactly(current);
            Unfilter(current, previous, channels, filter);
            Buffer.BlockCopy(current, 0, pixels, y * stride, stride);
            (previous, current) = (current, previous);
        }
        if (zlib.ReadByte() != -1) throw new InvalidDataException("PNG contains trailing pixel data.");
        return new(width, height, channels, pixels);
    }

    private static void Unfilter(byte[] row, byte[] previous, int channels, int filter)
    {
        for (int index = 0; index < row.Length; index++)
        {
            byte left = index >= channels ? row[index - channels] : (byte)0;
            byte above = previous[index];
            byte upperLeft = index >= channels ? previous[index - channels] : (byte)0;
            row[index] = filter switch
            {
                0 => row[index],
                1 => unchecked((byte)(row[index] + left)),
                2 => unchecked((byte)(row[index] + above)),
                3 => unchecked((byte)(row[index] + ((left + above) >> 1))),
                4 => unchecked((byte)(row[index] + Paeth(left, above, upperLeft))),
                _ => throw new InvalidDataException("PNG filter is unsupported."),
            };
        }
    }

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int aboveDistance = Math.Abs(estimate - above);
        int upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private sealed record DecodedPng(int Width, int Height, int Channels, byte[] Pixels);
}
