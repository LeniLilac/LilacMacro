using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionNodeEvidenceService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    internal static readonly PixelRect BarBand =
        RuntimeSearchRegionEvidenceCatalog.ExpeditionNodeBar.Bounds;
    internal static readonly PixelRect HoverLine =
        RuntimeSearchRegionEvidenceCatalog.ExpeditionNodeHoverLine.Bounds;
    internal static readonly PixelRect TooltipTitleBand =
        RuntimeSearchRegionEvidenceCatalog.ExpeditionNodeTooltip.Bounds;
    private const int InitialHoverSweepStep = 8;
    private const int LocalHoverSweepRadius = 32;
    private const int LocalHoverSweepStep = 4;
    private readonly ExpeditionOcrService _ocr = new(workspace, ocr);
    private readonly ExpeditionColorProfileStore _profiles = new();
    private ExpeditionNodeColorProfile? _profile;
    private ExpeditionNodeType? _hotCandidate;
    private int _hotStable;
    private int? _learnedMarkerToHoverOffsetX;

    public async Task<ExpeditionNodeType?> ObserveAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        RgbImage bar = (await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize, [BarBand], cancellationToken).ConfigureAwait(false)).Single().Image;
        PixelPoint? marker = FindCurrentMarker(bar);
        double? hue = marker is PixelPoint found ? CurrentBarHue(bar, found) : null;
        _profile ??= await _profiles.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (hue is double hotHue && _profile.Classify(hotHue) is ExpeditionNodeType hotNode)
        {
            _hotStable = _hotCandidate == hotNode ? _hotStable + 1 : 1;
            _hotCandidate = hotNode;
            if (_hotStable >= 2)
            {
                status?.Invoke($"EXPEDITION NODE {hotNode.ToString().ToUpperInvariant()} | COLOR HOTPATH");
                return hotNode;
            }
            status?.Invoke($"EXPEDITION NODE {hotNode.ToString().ToUpperInvariant()} | COLOR CONFIRM 1/2");
            return null;
        }
        _hotCandidate = null;
        _hotStable = 0;
        if (marker is null)
        {
            status?.Invoke("EXPEDITION NODE MARKER NOT FOUND");
            return null;
        }

        PixelPoint clientMarker = new(BarBand.X + marker.Value.X, BarBand.Y + marker.Value.Y);
        (ExpeditionNodeType Node, PixelPoint Hover)? calibrated =
            await ObserveTooltipAsync(clientMarker, device, cancellationToken).ConfigureAwait(false);
        ExpeditionNodeType? semantic = calibrated?.Node;
        if (semantic is null)
        {
            status?.Invoke("EXPEDITION NODE TOOLTIP HOVER SEARCH MISS");
            return null;
        }
        _learnedMarkerToHoverOffsetX = calibrated!.Value.Hover.X - clientMarker.X;
        if (hue is double learnedHue)
        {
            _profile.Learn(semantic.Value, learnedHue);
            try { await _profiles.SaveAsync(_profile, cancellationToken).ConfigureAwait(false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        status?.Invoke(
            $"EXPEDITION NODE {semantic.Value.ToString().ToUpperInvariant()} | HOVER OCR | " +
            $"OFFSET {_learnedMarkerToHoverOffsetX:+#;-#;0}");
        return semantic;
    }

    private async Task<(ExpeditionNodeType Node, PixelPoint Hover)?> ObserveTooltipAsync(
        PixelPoint marker,
        string device,
        CancellationToken cancellationToken)
    {
        foreach (PixelPoint probe in HoverProbePoints(marker, _learnedMarkerToHoverOffsetX))
        {
            await workspace.HoverRobloxAsync(
                DebugWorkflowCatalog.ClientSize, probe, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
                TooltipTitleBand, device, cancellationToken).ConfigureAwait(false);
            ExpeditionNodeType? semantic = ParseNode(regions.Select(region => region.Text));
            if (semantic is ExpeditionNodeType node) return (node, probe);
        }
        return null;
    }

    internal static IReadOnlyList<PixelPoint> HoverProbePoints(PixelPoint marker, int? learnedOffsetX)
    {
        int y = HoverLine.Y + HoverLine.Height / 2;
        if (learnedOffsetX is int cached)
        {
            int center = Math.Clamp(marker.X + cached, HoverLine.X, HoverLine.Right - 1);
            List<PixelPoint> local = [new(center, y)];
            for (int distance = LocalHoverSweepStep;
                 distance <= LocalHoverSweepRadius;
                 distance += LocalHoverSweepStep)
            {
                int right = Math.Min(HoverLine.Right - 1, center + distance);
                int left = Math.Max(HoverLine.X, center - distance);
                if (right != center && !local.Any(point => point.X == right)) local.Add(new(right, y));
                if (left != center && !local.Any(point => point.X == left)) local.Add(new(left, y));
            }
            return local;
        }

        int last = Math.Min(HoverLine.Right - 1, marker.X + LocalHoverSweepRadius);
        List<PixelPoint> initial = [];
        for (int x = HoverLine.X; x <= last; x += InitialHoverSweepStep)
            initial.Add(new(x, y));
        if (initial.Count == 0 || initial[^1].X != last) initial.Add(new(last, y));
        return initial;
    }

    internal static ExpeditionNodeType? ParseNode(IEnumerable<string> text)
    {
        string joined = string.Join(' ', text).ToLowerInvariant();
        ExpeditionNodeType[] matches = Enum.GetValues<ExpeditionNodeType>()
            .Where(node => joined.Contains(node.ToString().ToLowerInvariant(), StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static PixelPoint? FindCurrentMarker(RgbImage image)
    {
        PixelPoint? goldMarker = FindGoldMarker(image);
        if (goldMarker is not null) return goldMarker;

        int scanTop = Math.Clamp(17, 0, image.Size.Height - 1);
        int scanBottom = Math.Clamp(27, scanTop + 1, image.Size.Height);
        double?[] hues = new double?[image.Size.Width];
        for (int x = 0; x < image.Size.Width; x++)
        {
            List<double> column = [];
            for (int y = scanTop; y < scanBottom; y++)
            {
                Read(image, x, y, out byte red, out byte green, out byte blue);
                int brightness = Math.Max(red, Math.Max(green, blue));
                int spread = brightness - Math.Min(red, Math.Min(green, blue));
                if (brightness >= 150 && spread >= 55) column.Add(Hue(red, green, blue));
            }
            if (column.Count >= 2)
            {
                column.Sort();
                hues[x] = column[column.Count / 2];
            }
        }

        for (int start = 12; start < Math.Min(180, image.Size.Width - 60); start++)
        {
            double[] seed = hues.Skip(start).Take(24).OfType<double>().ToArray();
            if (seed.Length < 16) continue;
            double baseline = CircularAverage(seed);
            int mismatch = 0;
            for (int x = start + 24; x < image.Size.Width; x++)
            {
                bool agrees = hues[x] is double current &&
                    ExpeditionNodeColorProfile.HueDistance(current, baseline) <= 14;
                mismatch = agrees ? 0 : mismatch + 1;
                if (mismatch >= 6)
                {
                    int endpoint = x - mismatch;
                    if (endpoint - start >= 45) return new PixelPoint(endpoint, 21);
                    break;
                }
            }
        }
        return null;
    }

    private static PixelPoint? FindGoldMarker(RgbImage image)
    {
        List<(int X, int Y)> pixels = [];
        for (int y = 3; y < Math.Min(38, image.Size.Height); y++)
            for (int x = 60; x < Math.Min(300, image.Size.Width); x++)
            {
                Read(image, x, y, out byte red, out byte green, out byte blue);
                if (red >= 125 && green >= 95 && blue <= 105 && red >= green * 1.02 && green >= blue * 1.18)
                    pixels.Add((x, y));
            }
        if (pixels.Count == 0) return null;

        foreach (IGrouping<int, (int X, int Y)> cluster in pixels.GroupBy(pixel => pixel.X / 4)
                     .OrderByDescending(group => group.Key))
        {
            int centerX = checked((int)Math.Round(cluster.Average(pixel => pixel.X)));
            int minimumY = cluster.Min(pixel => pixel.Y);
            int maximumY = cluster.Max(pixel => pixel.Y);
            int nearby = pixels.Count(pixel => Math.Abs(pixel.X - centerX) <= 10 &&
                pixel.Y >= minimumY - 2 && pixel.Y <= maximumY + 2);
            if (maximumY - minimumY >= 4 && nearby >= 18)
                return new PixelPoint(centerX, checked((int)Math.Round(cluster.Average(pixel => pixel.Y))));
        }
        return null;
    }

    internal static double? CurrentBarHue(RgbImage image, PixelPoint marker)
    {
        List<double> hues = [];
        for (int y = Math.Max(0, marker.Y - 8); y <= Math.Min(image.Size.Height - 1, marker.Y + 2); y++)
            for (int x = Math.Max(0, marker.X - 120); x <= Math.Max(0, marker.X - 25); x++)
            {
                Read(image, x, y, out byte red, out byte green, out byte blue);
                double maximum = Math.Max(red, Math.Max(green, blue));
                double minimum = Math.Min(red, Math.Min(green, blue));
                double delta = maximum - minimum;
                if (maximum < 100 || delta < 38) continue;
                hues.Add(Hue(red, green, blue));
            }
        if (hues.Count < 12) return null;
        hues.Sort();
        return hues[hues.Count / 2];
    }

    private static double CircularAverage(IEnumerable<double> hues)
    {
        double x = 0;
        double y = 0;
        foreach (double hue in hues)
        {
            double radians = hue * Math.PI / 90;
            x += Math.Cos(radians);
            y += Math.Sin(radians);
        }
        double angle = Math.Atan2(y, x);
        if (angle < 0) angle += Math.PI * 2;
        return angle * 90 / Math.PI;
    }

    private static double Hue(byte red, byte green, byte blue)
    {
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double delta = maximum - minimum;
        double hue = maximum == red ? 60 * ((green - blue) / delta % 6)
            : maximum == green ? 60 * ((blue - red) / delta + 2)
            : 60 * ((red - green) / delta + 4);
        if (hue < 0) hue += 360;
        return hue / 2;
    }

    private static void Read(RgbImage image, int x, int y, out byte red, out byte green, out byte blue)
    {
        int offset = (y * image.Size.Width + x) * 3;
        red = image.Pixels[offset];
        green = image.Pixels[offset + 1];
        blue = image.Pixels[offset + 2];
    }
}
