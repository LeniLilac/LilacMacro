using LilacMacro.Core.Imaging;

namespace LilacMacro.Windows.Capture;

internal static class CaptureSurfaceConverter
{
    public static RgbImage ConvertBgra8ToRgb(
        byte[] bgra8,
        int surfaceWidth,
        int surfaceHeight,
        ScreenRegion crop)
    {
        if (bgra8.Length != checked(surfaceWidth * surfaceHeight * 4))
        {
            throw new ArgumentException("The BGRA8 buffer has an unexpected length.", nameof(bgra8));
        }
        if (crop.X < 0 || crop.Y < 0 || crop.Right > surfaceWidth || crop.Bottom > surfaceHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), "The client crop must fit inside the captured surface.");
        }

        byte[] rgb = new byte[checked(crop.Width * crop.Height * 3)];
        int target = 0;
        for (int y = crop.Y; y < crop.Bottom; y++)
        {
            int source = checked((y * surfaceWidth + crop.X) * 4);
            for (int x = 0; x < crop.Width; x++, source += 4, target += 3)
            {
                rgb[target] = bgra8[source + 2];
                rgb[target + 1] = bgra8[source + 1];
                rgb[target + 2] = bgra8[source];
            }
        }
        return new RgbImage(crop.Width, crop.Height, rgb, takeOwnership: true);
    }

    public static RgbImage ConvertScRgbRgba16ToRgb(
        byte[] rgba16,
        int surfaceWidth,
        int surfaceHeight,
        ScreenRegion crop,
        CaptureColorContext colorContext)
    {
        if (rgba16.Length != checked(surfaceWidth * surfaceHeight * 8))
        {
            throw new ArgumentException("The FP16 RGBA buffer has an unexpected length.", nameof(rgba16));
        }
        if (crop.X < 0 || crop.Y < 0 || crop.Right > surfaceWidth || crop.Bottom > surfaceHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), "The client crop must fit inside the captured surface.");
        }

        byte[] rgb = new byte[checked(crop.Width * crop.Height * 3)];
        int target = 0;
        for (int y = crop.Y; y < crop.Bottom; y++)
        {
            int source = checked((y * surfaceWidth + crop.X) * 8);
            for (int x = 0; x < crop.Width; x++, source += 8, target += 3)
            {
                float red = ReadFiniteHalf(rgba16, source);
                float green = ReadFiniteHalf(rgba16, source + 2);
                float blue = ReadFiniteHalf(rgba16, source + 4);
                ConvertToSrgbGamut(ref red, ref green, ref blue, colorContext);
                rgb[target] = LinearToSrgbByte(red);
                rgb[target + 1] = LinearToSrgbByte(green);
                rgb[target + 2] = LinearToSrgbByte(blue);
            }
        }
        return new RgbImage(crop.Width, crop.Height, rgb, takeOwnership: true);
    }

    private static float ReadFiniteHalf(byte[] source, int offset)
    {
        ushort bits = (ushort)(source[offset] | source[offset + 1] << 8);
        float value = (float)BitConverter.UInt16BitsToHalf(bits);
        return float.IsFinite(value) ? value : 0f;
    }

    private static void ConvertToSrgbGamut(
        ref float red,
        ref float green,
        ref float blue,
        CaptureColorContext colorContext)
    {
        const float shoulderStart = 0.8f;

        float referenceScale = colorContext.ScRgbReferenceScale;
        red *= referenceScale;
        green *= referenceScale;
        blue *= referenceScale;

        float luminance = 0.2126f * red + 0.7152f * green + 0.0722f * blue;
        if (luminance <= 0f)
        {
            red = green = blue = 0f;
            return;
        }

        float mappedLuminance = luminance;
        if (colorContext.AdvancedColorActive && luminance > shoulderStart)
        {
            float peak = colorContext.RelativeDisplayPeak;
            float capped = Math.Min(luminance, peak);
            float shoulderScale = Math.Max(1f, (peak - shoulderStart) / 3f);
            float numerator = 1f - MathF.Exp(-(capped - shoulderStart) / shoulderScale);
            float denominator = 1f - MathF.Exp(-(peak - shoulderStart) / shoulderScale);
            mappedLuminance = shoulderStart + (1f - shoulderStart) * numerator / denominator;
            float scale = mappedLuminance / luminance;
            red *= scale;
            green *= scale;
            blue *= scale;
        }
        else if (!colorContext.AdvancedColorActive && luminance > 1f)
        {
            mappedLuminance = 1f;
            float scale = 1f / luminance;
            red *= scale;
            green *= scale;
            blue *= scale;
        }

        float chromaScale = 1f;
        chromaScale = LimitChroma(red, mappedLuminance, chromaScale);
        chromaScale = LimitChroma(green, mappedLuminance, chromaScale);
        chromaScale = LimitChroma(blue, mappedLuminance, chromaScale);
        if (chromaScale < 1f)
        {
            red = mappedLuminance + (red - mappedLuminance) * chromaScale;
            green = mappedLuminance + (green - mappedLuminance) * chromaScale;
            blue = mappedLuminance + (blue - mappedLuminance) * chromaScale;
        }
    }

    private static float LimitChroma(float channel, float luminance, float currentScale)
    {
        if (channel > 1f && channel > luminance)
        {
            return Math.Min(currentScale, (1f - luminance) / (channel - luminance));
        }
        if (channel < 0f && channel < luminance)
        {
            return Math.Min(currentScale, luminance / (luminance - channel));
        }
        return currentScale;
    }

    private static byte LinearToSrgbByte(float value)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        float encoded = clamped <= 0.0031308f
            ? 12.92f * clamped
            : 1.055f * MathF.Pow(clamped, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }
}
