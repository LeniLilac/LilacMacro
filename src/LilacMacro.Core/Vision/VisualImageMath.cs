using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Vision;

internal static class VisualImageMath
{
    public static GrayImage CropAndResize(GrayImage source, PixelRect bounds, int width, int height)
    {
        byte[] output = new byte[checked(width * height)];
        ReadOnlySpan<byte> input = source.PixelSpan;

        for (int y = 0; y < height; y++)
        {
            double sourceY = bounds.Y + ((y + 0.5) * bounds.Height / height) - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sourceY), bounds.Y, bounds.Bottom - 1);
            int y1 = Math.Min(bounds.Bottom - 1, y0 + 1);
            double fy = Math.Clamp(sourceY - y0, 0, 1);

            for (int x = 0; x < width; x++)
            {
                double sourceX = bounds.X + ((x + 0.5) * bounds.Width / width) - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), bounds.X, bounds.Right - 1);
                int x1 = Math.Min(bounds.Right - 1, x0 + 1);
                double fx = Math.Clamp(sourceX - x0, 0, 1);
                double top = Lerp(input[y0 * source.Width + x0], input[y0 * source.Width + x1], fx);
                double bottom = Lerp(input[y1 * source.Width + x0], input[y1 * source.Width + x1], fx);
                output[y * width + x] = (byte)Math.Clamp((int)Math.Round(Lerp(top, bottom, fy)), 0, 255);
            }
        }

        return new GrayImage(width, height, output);
    }

    public static GrayImage Edges(GrayImage source)
    {
        byte[] output = new byte[checked(source.Width * source.Height)];
        ReadOnlySpan<byte> pixels = source.PixelSpan;
        for (int y = 1; y < source.Height - 1; y++)
        {
            for (int x = 1; x < source.Width - 1; x++)
            {
                int horizontal = Math.Abs(pixels[y * source.Width + x + 1] - pixels[y * source.Width + x - 1]);
                int vertical = Math.Abs(pixels[(y + 1) * source.Width + x] - pixels[(y - 1) * source.Width + x]);
                output[y * source.Width + x] = (byte)Math.Min(255, horizontal + vertical);
            }
        }

        return new GrayImage(source.Width, source.Height, output);
    }

    public static GrayImage Median(IReadOnlyList<GrayImage> images)
    {
        EnsureSameSize(images);
        byte[] values = new byte[images.Count];
        byte[] output = new byte[images[0].Pixels.Length];
        for (int index = 0; index < output.Length; index++)
        {
            for (int image = 0; image < images.Count; image++) values[image] = images[image].PixelSpan[index];
            Array.Sort(values);
            output[index] = values[values.Length / 2];
        }

        return new GrayImage(images[0].Width, images[0].Height, output);
    }

    public static (double[] StandardDeviations, double Mean) StandardDeviation(IReadOnlyList<GrayImage> images)
    {
        EnsureSameSize(images);
        double[] deviations = new double[images[0].Pixels.Length];
        double total = 0;
        for (int index = 0; index < deviations.Length; index++)
        {
            double mean = 0;
            for (int image = 0; image < images.Count; image++) mean += images[image].PixelSpan[index];
            mean /= images.Count;
            double variance = 0;
            for (int image = 0; image < images.Count; image++)
            {
                double delta = images[image].PixelSpan[index] - mean;
                variance += delta * delta;
            }

            deviations[index] = Math.Sqrt(variance / images.Count);
            total += deviations[index];
        }

        return (deviations, total / deviations.Length);
    }

    public static GrayImage Reliability(int width, int height, IReadOnlyList<double> deviations, double scale)
    {
        byte[] output = new byte[deviations.Count];
        for (int index = 0; index < output.Length; index++)
        {
            double ratio = deviations[index] / scale;
            output[index] = (byte)Math.Clamp((int)Math.Round(255 / (1 + ratio * ratio)), 8, 255);
        }

        return new GrayImage(width, height, output);
    }

    public static double MeanAbsoluteDistance(GrayImage first, GrayImage second)
    {
        EnsureSameSize([first, second]);
        double total = 0;
        for (int index = 0; index < first.PixelSpan.Length; index++)
        {
            total += Math.Abs(first.PixelSpan[index] - second.PixelSpan[index]);
        }

        return total / (first.PixelSpan.Length * 255d);
    }

    private static void EnsureSameSize(IReadOnlyList<GrayImage> images)
    {
        if (images.Count == 0) throw new ArgumentException("At least one image is required.", nameof(images));
        if (images.Any(image => image.Width != images[0].Width || image.Height != images[0].Height))
        {
            throw new ArgumentException("Images must have identical dimensions.", nameof(images));
        }
    }

    private static double Lerp(double first, double second, double amount) => first + (second - first) * amount;
}
