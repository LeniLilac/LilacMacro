using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows.Capture;

internal sealed record CaptureAtlasEntry(int RequestIndex, PixelRect Source, ScreenRegion Atlas);

internal sealed record CaptureAtlasLayout(int Width, int Height, IReadOnlyList<CaptureAtlasEntry> Entries)
{
    private const int MaximumRegions = 64;
    private const int MaximumTextureExtent = 16384;

    public static CaptureAtlasLayout Create(
        int clientWidth,
        int clientHeight,
        IReadOnlyList<PixelRect> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (clientWidth < 1) throw new ArgumentOutOfRangeException(nameof(clientWidth));
        if (clientHeight < 1) throw new ArgumentOutOfRangeException(nameof(clientHeight));
        if (regions.Count is < 1 or > MaximumRegions)
        {
            throw new ArgumentOutOfRangeException(nameof(regions), $"A detector capture requires 1 to {MaximumRegions} regions.");
        }

        PixelSize client = new(clientWidth, clientHeight);
        long totalArea = 0;
        int widest = 0;
        for (int index = 0; index < regions.Count; index++)
        {
            PixelRect region = regions[index];
            if (!region.IsInside(client))
            {
                throw new ArgumentOutOfRangeException(nameof(regions), $"Detector region {index} is outside the Roblox client.");
            }
            totalArea = checked(totalArea + (long)region.Width * region.Height);
            widest = Math.Max(widest, region.Width);
        }
        if (totalArea > (long)clientWidth * clientHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(regions), "Detector regions exceed one client frame of requested pixels.");
        }

        int targetWidth = Math.Max(widest, checked((int)Math.Ceiling(Math.Sqrt(totalArea))));
        targetWidth = Math.Min(targetWidth, Math.Min(clientWidth, MaximumTextureExtent));
        var ordered = regions
            .Select((region, index) => (Region: region, Index: index))
            .OrderByDescending(item => item.Region.Height)
            .ThenByDescending(item => item.Region.Width)
            .ThenBy(item => item.Index);

        List<CaptureAtlasEntry> entries = [];
        int x = 0;
        int y = 0;
        int rowHeight = 0;
        int usedWidth = 0;
        foreach ((PixelRect region, int index) in ordered)
        {
            if (x > 0 && x + region.Width > targetWidth)
            {
                y = checked(y + rowHeight);
                x = 0;
                rowHeight = 0;
            }
            entries.Add(new CaptureAtlasEntry(index, region, new ScreenRegion(x, y, region.Width, region.Height)));
            x = checked(x + region.Width);
            rowHeight = Math.Max(rowHeight, region.Height);
            usedWidth = Math.Max(usedWidth, x);
        }

        int height = checked(y + rowHeight);
        if (usedWidth > MaximumTextureExtent || height > MaximumTextureExtent)
        {
            throw new ArgumentOutOfRangeException(nameof(regions), "The detector atlas exceeds the GPU texture limit.");
        }
        return new CaptureAtlasLayout(
            usedWidth,
            height,
            entries.OrderBy(entry => entry.RequestIndex).ToArray());
    }
}
