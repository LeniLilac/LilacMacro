using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Core.Placements;

public enum UnitPanelDpsKind
{
    Phantom,
    Physical,
}

public sealed record UnitPanelDpsCapturePlan(
    PixelRect Region,
    PixelRect TextBand,
    PixelRect CoreBand)
{
    public static UnitPanelDpsCapturePlan Create(
        UnitPanelLayout layout,
        PixelSize clientSize)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!layout.DpsText.IsInside(clientSize))
            throw new ArgumentException("DPS OCR bounds must be inside the Roblox client.", nameof(layout));

        int unit = Math.Max(1, Math.Abs(layout.SellText.Center.X - layout.PriorityText.Center.X));
        int horizontalPadding = Math.Max(1, Scaled(unit, 0.04));
        int verticalPadding = Math.Max(1, Scaled(unit, 0.04));
        int width = Math.Max(
            Scaled(unit, 0.60),
            layout.DpsText.Width + horizontalPadding * 2);
        int height = Math.Max(
            Scaled(unit, 0.28),
            layout.DpsText.Height + verticalPadding * 2);
        width = Math.Min(width, clientSize.Width);
        height = Math.Min(height, clientSize.Height);

        PixelRect region = Centered(layout.DpsText.Center, width, height, clientSize);
        PixelRect textBand = Translate(layout.DpsText, region);
        int corePadding = Math.Max(1, Scaled(unit, 0.02));
        PixelRect coreBand = Inflate(textBand, corePadding, new PixelSize(region.Width, region.Height));
        return new UnitPanelDpsCapturePlan(region, textBand, coreBand);
    }

    private static PixelRect Centered(
        PixelPoint center,
        int width,
        int height,
        PixelSize bounds)
    {
        int left = Math.Clamp(center.X - width / 2, 0, bounds.Width - width);
        int top = Math.Clamp(center.Y - height / 2, 0, bounds.Height - height);
        return new PixelRect(left, top, width, height);
    }

    private static PixelRect Translate(PixelRect rectangle, PixelRect origin) => new(
        rectangle.X - origin.X,
        rectangle.Y - origin.Y,
        rectangle.Width,
        rectangle.Height);

    private static PixelRect Inflate(PixelRect rectangle, int amount, PixelSize bounds)
    {
        int left = Math.Max(0, rectangle.X - amount);
        int top = Math.Max(0, rectangle.Y - amount);
        int right = Math.Min(bounds.Width, rectangle.Right + amount);
        int bottom = Math.Min(bounds.Height, rectangle.Bottom + amount);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static int Scaled(int value, double factor) =>
        Math.Max(1, (int)Math.Round(value * factor));
}

public sealed record UnitPanelDpsImageMatch(
    bool IsExact,
    double ExactFraction,
    int MatchingPixels,
    int ComparedPixels);

public sealed class UnitPanelDpsFingerprint
{
    private readonly byte[] _referencePixels;
    private readonly bool[] _mask;

    internal UnitPanelDpsFingerprint(
        PixelSize size,
        byte[] referencePixels,
        bool[] mask,
        int phantomSampleCount,
        int physicalSampleCount)
    {
        Size = size;
        _referencePixels = referencePixels;
        _mask = mask;
        PhantomSampleCount = phantomSampleCount;
        PhysicalSampleCount = physicalSampleCount;
        ComparedPixels = mask.Count(value => value);
    }

    public PixelSize Size { get; }

    public int ComparedPixels { get; }

    public int PhantomSampleCount { get; }

    public int PhysicalSampleCount { get; }

    public UnitPanelDpsImageMatch Match(RgbImage candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Size != Size || ComparedPixels == 0)
            return new UnitPanelDpsImageMatch(false, 0, 0, ComparedPixels);

        int matching = 0;
        for (int pixel = 0; pixel < _mask.Length; pixel++)
        {
            if (!_mask[pixel]) continue;
            int offset = pixel * 3;
            if (_referencePixels[offset] == candidate.Pixels[offset] &&
                _referencePixels[offset + 1] == candidate.Pixels[offset + 1] &&
                _referencePixels[offset + 2] == candidate.Pixels[offset + 2])
                matching++;
        }

        double fraction = matching / (double)ComparedPixels;
        return new UnitPanelDpsImageMatch(
            matching == ComparedPixels,
            fraction,
            matching,
            ComparedPixels);
    }
}

public sealed class UnitPanelDpsFingerprintBuilder
{
    private const int MinimumPhantomSamples = 2;
    private const int MinimumSamplesForCrossStateMask = 2;
    private const int MinimumTextPixels = 3;
    private const int MaximumSamples = 8;
    private readonly PixelSize _sampleSize;
    private readonly PixelRect _textBand;
    private readonly PixelRect _coreBand;
    private readonly List<byte[]> _phantomSamples = [];
    private readonly List<byte[]> _physicalSamples = [];
    private UnitPanelDpsFingerprint? _fingerprint;

    public UnitPanelDpsFingerprintBuilder(UnitPanelDpsCapturePlan plan, PixelSize sampleSize)
    {
        ArgumentNullException.ThrowIfNull(plan);
        PixelSize sampleBounds = new(sampleSize.Width, sampleSize.Height);
        if (!plan.TextBand.IsInside(sampleBounds) || !plan.CoreBand.IsInside(sampleBounds))
            throw new ArgumentException("DPS capture plan must fit inside its sample image.", nameof(plan));

        _sampleSize = sampleSize;
        _textBand = plan.TextBand;
        _coreBand = plan.CoreBand;
    }

    public UnitPanelDpsFingerprint? Fingerprint => _fingerprint;

    public int PhantomSampleCount => _phantomSamples.Count;

    public int PhysicalSampleCount => _physicalSamples.Count;

    public void AddSample(UnitPanelDpsKind kind, RgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Size != _sampleSize)
            throw new ArgumentException("DPS sample size does not match the calibrated capture region.", nameof(image));

        List<byte[]> samples = kind == UnitPanelDpsKind.Phantom
            ? _phantomSamples
            : _physicalSamples;
        samples.Add(image.Pixels.ToArray());
        if (samples.Count > MaximumSamples) samples.RemoveAt(0);

        if (BuildFingerprint() is { } fingerprint)
            _fingerprint = fingerprint;
    }

    private UnitPanelDpsFingerprint? BuildFingerprint()
    {
        if (_phantomSamples.Count < MinimumPhantomSamples) return null;

        byte[] reference = _phantomSamples[0];
        bool[] mask = new bool[_sampleSize.Width * _sampleSize.Height];
        int textPixels = 0;
        int physicalSamples = _physicalSamples.Count;
        bool useCrossStateMask = physicalSamples >= MinimumSamplesForCrossStateMask;

        for (int pixel = 0; pixel < mask.Length; pixel++)
        {
            if (!StableAcross(_phantomSamples, pixel, reference)) continue;
            int x = pixel % _sampleSize.Width;
            int y = pixel / _sampleSize.Width;
            bool inText = Contains(_textBand, x, y);
            bool inCore = Contains(_coreBand, x, y);
            if (!inText && !inCore) continue;

            if (!useCrossStateMask)
            {
                mask[pixel] = true;
                if (inText) textPixels++;
                continue;
            }

            bool physicalStable = StableAcross(_physicalSamples, pixel, _physicalSamples[0]);
            bool differsFromPhantom = !SamePixel(reference, _physicalSamples[0], pixel);
            bool glyph = inText && physicalStable && differsFromPhantom;
            bool pill = inCore && physicalStable && !differsFromPhantom;
            mask[pixel] = glyph || pill;
            if (glyph) textPixels++;
        }

        if (textPixels < MinimumTextPixels) return null;
        return new UnitPanelDpsFingerprint(
            _sampleSize,
            reference.ToArray(),
            mask,
            _phantomSamples.Count,
            _physicalSamples.Count);
    }

    private static bool StableAcross(IReadOnlyList<byte[]> samples, int pixel, byte[] reference) =>
        samples.All(sample => SamePixel(reference, sample, pixel));

    private static bool SamePixel(byte[] first, byte[] second, int pixel)
    {
        int offset = pixel * 3;
        return first[offset] == second[offset] &&
            first[offset + 1] == second[offset + 1] &&
            first[offset + 2] == second[offset + 2];
    }

    private static bool Contains(PixelRect region, int x, int y) =>
        x >= region.X && x < region.Right && y >= region.Y && y < region.Bottom;
}
