using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Runtime.Normalization;

internal readonly record struct UiScalePanelMatch(
    bool Visible,
    bool Settled,
    double RenderedScale,
    double Confidence,
    PixelPoint ClosePoint,
    PixelRect PanelBounds);

internal static class UiScalePanelDetector
{
    private const int ClientWidth = 1366;
    private const int ClientHeight = 700;
    private const double PanelCenterX = 683;
    private const double PanelCenterY = 350;
    private const double BaseCloseOffsetX = 430;
    private const double BaseCloseOffsetY = 237.5;
    private const double BasePanelHalfWidth = 447;
    private const double BasePanelHalfHeight = 253;
    private static readonly PixelRect CloseSearch = new(980, 32, 256, 162);
    private static readonly PixelRect GearSearch = new(210, 12, 41, 44);

    public static PixelPoint? DetectSettingsGear(RgbImage image)
    {
        Validate(image);
        int white = 0;
        int dark = 0;
        long whiteX = 0;
        long whiteY = 0;
        for (int y = GearSearch.Y; y < GearSearch.Bottom; y++)
        {
            for (int x = GearSearch.X; x < GearSearch.Right; x++)
            {
                Read(image, x, y, out byte red, out byte green, out byte blue);
                if (IsNeutralWhite(red, green, blue))
                {
                    white++;
                    whiteX += x;
                    whiteY += y;
                }
                if (IsDark(red, green, blue)) dark++;
            }
        }

        int area = GearSearch.Width * GearSearch.Height;
        if (white is < 150 or > 310 || dark < area * 0.65)
            return null;

        PixelPoint center = new(
            checked((int)Math.Round(whiteX / (double)white)),
            checked((int)Math.Round(whiteY / (double)white)));
        return center.X is >= 224 and <= 236 && center.Y is >= 28 and <= 40
            ? center
            : null;
    }

    public static UiScalePanelMatch DetectPanel(RgbImage image)
    {
        Validate(image);
        foreach (ColorComponent close in FindRedComponents(image)
                     .Where(IsPlausibleClose)
                     .OrderByDescending(component => component.Count))
        {
            double closeX = (close.MinimumX + close.MaximumX) / 2d;
            double closeY = (close.MinimumY + close.MaximumY) / 2d;
            double scaleX = (closeX - PanelCenterX) / BaseCloseOffsetX;
            double scaleY = (PanelCenterY - closeY) / BaseCloseOffsetY;
            double scale = (scaleX + scaleY) / 2d;
            if (scale is < 0.75 or > 1.25) continue;

            PixelRect panel = PanelBounds(scale);
            double left = CyanFraction(image, new PixelRect(
                Math.Max(0, panel.X - 2), panel.Y + 40, 5, panel.Height - 50));
            double right = CyanFraction(image, new PixelRect(
                panel.Right - 3, panel.Y + 40, 5, panel.Height - 50));
            double bottom = CyanFraction(image, new PixelRect(
                panel.X + 100, panel.Bottom - 3, panel.Width - 110, 5));
            if (left < 0.16 || right < 0.16 || bottom < 0.16) continue;

            double agreement = Math.Abs(scaleX - scaleY);
            double border = Math.Min(left, Math.Min(right, bottom));
            double confidence = Math.Clamp(
                0.58 + Math.Min(0.22, close.Count / 2500d) + Math.Min(0.20, border / 2d),
                0,
                1);
            return new UiScalePanelMatch(
                true,
                agreement <= 0.035,
                scale,
                confidence,
                new PixelPoint(checked((int)Math.Round(closeX)), checked((int)Math.Round(closeY))),
                panel);
        }
        return default;
    }

    public static bool IsCanonicalRenderedScale(double scale) =>
        double.IsFinite(scale) && scale is >= 0.98 and <= 1.02;

    private static PixelRect PanelBounds(double scale)
    {
        int left = checked((int)Math.Round(PanelCenterX - BasePanelHalfWidth * scale));
        int top = checked((int)Math.Round(PanelCenterY - BasePanelHalfHeight * scale));
        int right = checked((int)Math.Round(PanelCenterX + BasePanelHalfWidth * scale));
        int bottom = checked((int)Math.Round(PanelCenterY + BasePanelHalfHeight * scale));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static bool IsPlausibleClose(ColorComponent component) =>
        component.Width is >= 24 and <= 58 &&
        component.Height is >= 24 and <= 58 &&
        component.Count >= 180 &&
        component.Width / (double)component.Height is >= 0.72 and <= 1.38;

    private static IReadOnlyList<ColorComponent> FindRedComponents(RgbImage image)
    {
        bool[] matches = new bool[CloseSearch.Width * CloseSearch.Height];
        for (int y = 0; y < CloseSearch.Height; y++)
        {
            for (int x = 0; x < CloseSearch.Width; x++)
            {
                Read(image, CloseSearch.X + x, CloseSearch.Y + y, out byte red, out byte green, out byte blue);
                matches[y * CloseSearch.Width + x] = IsRed(red, green, blue);
            }
        }

        List<ColorComponent> components = [];
        Queue<int> queue = new();
        for (int index = 0; index < matches.Length; index++)
        {
            if (!matches[index]) continue;
            matches[index] = false;
            queue.Enqueue(index);
            int minimumX = CloseSearch.Width;
            int minimumY = CloseSearch.Height;
            int maximumX = 0;
            int maximumY = 0;
            int count = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % CloseSearch.Width;
                int y = current / CloseSearch.Width;
                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
                count++;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }
            components.Add(new ColorComponent(
                minimumX + CloseSearch.X,
                minimumY + CloseSearch.Y,
                maximumX + CloseSearch.X,
                maximumY + CloseSearch.Y,
                count));

            void Visit(int x, int y)
            {
                if (x < 0 || y < 0 || x >= CloseSearch.Width || y >= CloseSearch.Height) return;
                int neighbor = y * CloseSearch.Width + x;
                if (!matches[neighbor]) return;
                matches[neighbor] = false;
                queue.Enqueue(neighbor);
            }
        }
        return components;
    }

    private static double CyanFraction(RgbImage image, PixelRect region)
    {
        int matching = 0;
        for (int y = region.Y; y < region.Bottom; y++)
        {
            for (int x = region.X; x < region.Right; x++)
            {
                Read(image, x, y, out byte red, out byte green, out byte blue);
                if (green >= 70 && blue >= 80 && green >= red * 1.2 && blue >= red * 1.15)
                    matching++;
            }
        }
        return matching / (double)(region.Width * region.Height);
    }

    private static bool IsRed(byte red, byte green, byte blue) =>
        red >= 100 && red >= green * 1.35 && red >= blue * 1.15;

    private static bool IsNeutralWhite(byte red, byte green, byte blue)
    {
        int maximum = Math.Max(red, Math.Max(green, blue));
        int minimum = Math.Min(red, Math.Min(green, blue));
        return minimum >= 205 && maximum - minimum <= 28;
    }

    private static bool IsDark(byte red, byte green, byte blue) =>
        red <= 65 && green <= 70 && blue <= 75;

    private static void Read(RgbImage image, int x, int y, out byte red, out byte green, out byte blue)
    {
        int pixel = checked((y * image.Size.Width + x) * 3);
        red = image.Pixels[pixel];
        green = image.Pixels[pixel + 1];
        blue = image.Pixels[pixel + 2];
    }

    private static void Validate(RgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Size != new PixelSize(ClientWidth, ClientHeight))
            throw new InvalidDataException($"UI Scale vision requires a {ClientWidth} by {ClientHeight} RGB client image.");
    }

    private readonly record struct ColorComponent(
        int MinimumX,
        int MinimumY,
        int MaximumX,
        int MaximumY,
        int Count)
    {
        public int Width => MaximumX - MinimumX + 1;
        public int Height => MaximumY - MinimumY + 1;
    }
}
