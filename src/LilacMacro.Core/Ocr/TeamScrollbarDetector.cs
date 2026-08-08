using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Core.Ocr;

public sealed record TeamScrollbarEndpoints(PixelRect TopBounds, PixelRect BottomBounds);

public static class TeamScrollbarDetector
{
    private const int MinimumBrightness = 112;
    private const int MaximumBrightness = 168;
    private const int MaximumChannelSpread = 10;
    private const int MinimumWidth = 3;
    private const int MaximumWidth = 20;

    public static PixelRect CreateSearchRegion(TeamSwapLayout layout, PixelSize clientSize)
    {
        ArgumentNullException.ThrowIfNull(layout);
        int rightmostButton = layout.Buttons.Max(button => button.Bounds.Right);
        int x = Math.Clamp(rightmostButton + 2, 0, clientSize.Width - 1);
        int width = Math.Min(clientSize.Width - x, Math.Max(28, layout.RowPitch / 2));
        int y = Math.Clamp(layout.TitleBounds.Y, 0, clientSize.Height - 1);
        return new PixelRect(x, y, width, clientSize.Height - y);
    }

    public static TeamScrollbarEndpoints? TryCalibrate(
        IReadOnlyList<RgbImage> topFrames,
        IReadOnlyList<RgbImage> bottomFrames,
        PixelRect searchRegion)
    {
        if (topFrames.Count < 2 || bottomFrames.Count < 2) return null;
        PixelRect[] top = StableCandidates(topFrames[0], topFrames[1], searchRegion).ToArray();
        PixelRect[] bottom = StableCandidates(bottomFrames[0], bottomFrames[1], searchRegion).ToArray();

        return top
            .SelectMany(topBounds => bottom.Select(bottomBounds => new
            {
                Top = topBounds,
                Bottom = bottomBounds,
                ShapeError = ShapeError(topBounds, bottomBounds),
            }))
            .Where(pair => pair.Bottom.Center.Y - pair.Top.Center.Y >=
                Math.Max(30, pair.Top.Height / 2))
            .Where(pair => Math.Abs(pair.Bottom.Center.X - pair.Top.Center.X) <= 4)
            .Where(pair => pair.ShapeError <= 12)
            .OrderBy(pair => pair.ShapeError)
            .ThenByDescending(pair => pair.Top.Width * pair.Top.Height)
            .Select(pair => new TeamScrollbarEndpoints(pair.Top, pair.Bottom))
            .FirstOrDefault();
    }

    private static IEnumerable<PixelRect> StableCandidates(
        RgbImage first,
        RgbImage second,
        PixelRect offset)
    {
        if (first.Size != second.Size || first.Size.Width != offset.Width || first.Size.Height != offset.Height)
            return [];
        PixelRect[] firstCandidates = FindCandidates(first, offset).ToArray();
        PixelRect[] secondCandidates = FindCandidates(second, offset).ToArray();
        return firstCandidates
            .Where(candidate => secondCandidates.Any(other =>
                Math.Abs(candidate.Center.X - other.Center.X) <= 2 &&
                Math.Abs(candidate.Center.Y - other.Center.Y) <= 3 &&
                ShapeError(candidate, other) <= 8));
    }

    private static IEnumerable<PixelRect> FindCandidates(RgbImage image, PixelRect offset)
    {
        int width = image.Size.Width;
        int height = image.Size.Height;
        bool[] mask = new bool[checked(width * height)];
        for (int index = 0; index < mask.Length; index++)
        {
            int pixel = index * 3;
            byte red = image.Pixels[pixel];
            byte green = image.Pixels[pixel + 1];
            byte blue = image.Pixels[pixel + 2];
            int minimum = Math.Min(red, Math.Min(green, blue));
            int maximum = Math.Max(red, Math.Max(green, blue));
            int brightness = (red + green + blue) / 3;
            mask[index] = maximum - minimum <= MaximumChannelSpread &&
                brightness is >= MinimumBrightness and <= MaximumBrightness;
        }

        bool[] visited = new bool[mask.Length];
        Queue<int> queue = new();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || visited[start]) continue;
            visited[start] = true;
            queue.Enqueue(start);
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % width;
                int y = current / width;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            int componentWidth = maxX - minX + 1;
            int componentHeight = maxY - minY + 1;
            int minimumHeight = Math.Max(24, height / 20);
            if (componentWidth is >= MinimumWidth and <= MaximumWidth &&
                componentHeight >= minimumHeight &&
                componentHeight >= componentWidth * 4 &&
                componentHeight <= height / 2)
            {
                yield return new PixelRect(
                    offset.X + minX,
                    offset.Y + minY,
                    componentWidth,
                    componentHeight);
            }

            void Visit(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return;
                int index = y * width + x;
                if (!mask[index] || visited[index]) return;
                visited[index] = true;
                queue.Enqueue(index);
            }
        }
    }

    private static int ShapeError(PixelRect first, PixelRect second) =>
        Math.Abs(first.Width - second.Width) + Math.Abs(first.Height - second.Height);
}
