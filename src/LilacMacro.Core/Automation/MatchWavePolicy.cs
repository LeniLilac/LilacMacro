using System.Text.RegularExpressions;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Core.Automation;

public sealed record MatchWaveObservation(int Wave, PixelRect NumberBounds, PixelRect LabelBounds);

public static partial class MatchWavePolicy
{
    public const int DefaultTarget = 140;
    public const int MaximumTarget = 999;
    public const int RequiredConsecutiveObservations = 2;

    public static MatchWaveObservation? TryObserve(
        RgbImage image,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(regions);
        OcrTextRegion[] trusted = regions
            .Where(region => region.RecognitionConfidence >= 0.85 && !string.IsNullOrWhiteSpace(region.Text))
            .ToArray();
        foreach (OcrTextRegion region in trusted)
        {
            Match match = CombinedWave().Match(Normalize(region.Text));
            if (!match.Success || !TryWave(match, out int wave)) continue;
            if (HasCapsuleStructure(image, region.Bounds, region.Bounds))
                return new MatchWaveObservation(wave, region.Bounds, region.Bounds);
        }

        OcrTextRegion[] labels = trusted.Where(region => Normalize(region.Text).Contains("wave", StringComparison.Ordinal)).ToArray();
        OcrTextRegion[] numbers = trusted.Where(region => NumberOnly().IsMatch(Normalize(region.Text))).ToArray();
        foreach (OcrTextRegion label in labels)
        {
            foreach (OcrTextRegion number in numbers)
            {
                if (!SameRow(number.Bounds, label.Bounds) || number.Bounds.X >= label.Bounds.Right) continue;
                int gap = label.Bounds.X - number.Bounds.Right;
                if (gap is < -8 or > 24 || !int.TryParse(Normalize(number.Text), out int wave) || !Valid(wave)) continue;
                if (HasCapsuleStructure(image, number.Bounds, label.Bounds))
                    return new MatchWaveObservation(wave, number.Bounds, label.Bounds);
            }
        }
        return null;
    }

    public static bool HasReachedTarget(int target, params int?[] observations)
    {
        if (!Valid(target) || observations.Length < RequiredConsecutiveObservations) return false;
        int?[] tail = observations[^RequiredConsecutiveObservations..];
        return tail.All(value => value is >= 1 && value >= target) &&
               tail.Zip(tail.Skip(1), (left, right) => right!.Value >= left!.Value).All(value => value);
    }

    private static bool HasCapsuleStructure(RgbImage image, PixelRect number, PixelRect label)
    {
        PixelRect content = Union(number, label);
        PixelRect text = Clip(content, image.Size);
        PixelRect context = Clip(Inflate(content, 8, 7), image.Size);
        PixelRect iconBand = Clip(new PixelRect(
            Math.Max(0, content.X - 34),
            Math.Max(0, content.Y - 9),
            Math.Min(image.Size.Width, content.Right + 2) - Math.Max(0, content.X - 34),
            Math.Min(image.Size.Height, content.Bottom + 9) - Math.Max(0, content.Y - 9)), image.Size);
        if (text.Width < 12 || text.Height < 8 || context.Width < 24 || context.Height < 14) return false;

        int brightNeutral = Count(image, text, static (r, g, b) =>
            r >= 155 && g >= 155 && b >= 155 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 45);
        int dark = Count(image, context, static (r, g, b) => (r + g + b) / 3 <= 72);
        int blue = Count(image, iconBand, static (r, g, b) => b >= 75 && b >= r + 20 && b >= g + 4);
        return brightNeutral >= 12 &&
               dark >= Math.Max(80, context.Width * context.Height * 30 / 100) &&
               blue >= 10;
    }

    private static int Count(RgbImage image, PixelRect bounds, Func<int, int, int, bool> predicate)
    {
        int count = 0;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            int row = y * image.Size.Width * 3;
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                int offset = row + x * 3;
                if (predicate(image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2])) count++;
            }
        }
        return count;
    }

    private static bool SameRow(PixelRect left, PixelRect right)
    {
        int overlap = Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Y, right.Y);
        return overlap >= Math.Min(left.Height, right.Height) / 2;
    }

    private static bool TryWave(Match match, out int wave) =>
        int.TryParse(match.Groups["before"].Success ? match.Groups["before"].Value : match.Groups["after"].Value, out wave) && Valid(wave);

    private static bool Valid(int value) => value is >= 1 and <= MaximumTarget;

    private static string Normalize(string value) => new(value
        .Where(character => char.IsAsciiLetterOrDigit(character))
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static PixelRect Union(PixelRect first, PixelRect second)
    {
        int left = Math.Min(first.X, second.X);
        int top = Math.Min(first.Y, second.Y);
        int right = Math.Max(first.Right, second.Right);
        int bottom = Math.Max(first.Bottom, second.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static PixelRect Inflate(PixelRect value, int horizontal, int vertical) => new(
        value.X - horizontal,
        value.Y - vertical,
        value.Width + horizontal * 2,
        value.Height + vertical * 2);

    private static PixelRect Clip(PixelRect value, PixelSize size)
    {
        int left = Math.Clamp(value.X, 0, size.Width);
        int top = Math.Clamp(value.Y, 0, size.Height);
        int right = Math.Clamp(value.Right, left, size.Width);
        int bottom = Math.Clamp(value.Bottom, top, size.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    [GeneratedRegex(@"^(?:(?<before>\d{1,3})wave|wave(?<after>\d{1,3}))$")]
    private static partial Regex CombinedWave();

    [GeneratedRegex(@"^\d{1,3}$")]
    private static partial Regex NumberOnly();
}
