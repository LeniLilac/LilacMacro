using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.App.Debugging;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Tests;

public sealed class MatchWavePolicyTests
{
    [Fact]
    public void RecordedWaveCapsulesRequireStructureAndParseSplitOrMergedOcr()
    {
        string root = FindDataset("match-wave-roi-20260816-002856");
        (OcrTextRegion[] Regions, int Wave)[] evidence =
        [
            ([Region(34, 26, 24, 13, "139"), Region(65, 27, 34, 12, "wave")], 139),
            ([Region(0, 19, 97, 24, "140wave")], 140),
            ([Region(0, 28, 15, 10, "Ith", 0.94), Region(52, 26, 21, 13, "140"), Region(78, 28, 26, 9, "wave")], 140),
        ];

        for (int index = 0; index < evidence.Length; index++)
        {
            RgbImage full = LoadPng(Path.Combine(root, "images", $"frame-{index + 1:D4}.png"));
            RgbImage crop = Crop(full, RuntimeSearchRegionEvidenceCatalog.MatchWaveCounter.Bounds);
            MatchWaveObservation observation = Assert.IsType<MatchWaveObservation>(
                MatchWavePolicy.TryObserve(crop, evidence[index].Regions));
            Assert.Equal(evidence[index].Wave, observation.Wave);
        }
    }

    [Fact]
    public void TextWithoutTheRecordedCapsuleStructureCannotOwnWaveState()
    {
        RgbImage dark = new(110, 52, new byte[110 * 52 * 3], takeOwnership: true);
        Assert.Null(MatchWavePolicy.TryObserve(
            dark,
            [Region(34, 26, 24, 13, "140"), Region(65, 27, 34, 12, "wave")]));
    }

    [Fact]
    public void ThresholdRequiresTwoNondecreasingFreshObservations()
    {
        Assert.False(MatchWavePolicy.HasReachedTarget(140, 139, 140));
        Assert.False(MatchWavePolicy.HasReachedTarget(140, 140, 139));
        Assert.True(MatchWavePolicy.HasReachedTarget(140, 140, 140));
        Assert.True(MatchWavePolicy.HasReachedTarget(140, 140, 141));
    }

    private static OcrTextRegion Region(int x, int y, int width, int height, string text, double confidence = 0.99) => new()
    {
        Bounds = new PixelRect(x, y, width, height),
        Text = text,
        RecognitionConfidence = confidence,
    };

    private static string FindDataset(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "LilacMacro.App", "Assets", "RuntimeEvidence", name);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(name);
    }

    private static RgbImage Crop(RgbImage image, PixelRect region)
    {
        byte[] pixels = new byte[region.Width * region.Height * 3];
        for (int y = 0; y < region.Height; y++)
            Buffer.BlockCopy(image.Pixels, ((region.Y + y) * image.Size.Width + region.X) * 3,
                pixels, y * region.Width * 3, region.Width * 3);
        return new RgbImage(region.Width, region.Height, pixels, takeOwnership: true);
    }

    private static RgbImage LoadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame source = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        FormatConvertedBitmap converted = new(source, PixelFormats.Rgb24, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 3];
        converted.CopyPixels(pixels, converted.PixelWidth * 3, 0);
        return new RgbImage(converted.PixelWidth, converted.PixelHeight, pixels, takeOwnership: true);
    }
}
