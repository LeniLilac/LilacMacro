using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Vision;

namespace LilacMacro.App.Views;

internal static class ReviewVisualMatchService
{
    public static ReviewVisualMatchResult Run(
        DatasetLocation dataset,
        DatasetFrame activeFrame,
        OcrTrial selectedTrial,
        OcrTextRegion selected)
    {
        string label = selected.Text.Trim();
        if (label.Length == 0)
        {
            throw new InvalidOperationException("Label this box before testing image matching.");
        }
        if (!selected.IsVisualAnchor)
        {
            throw new InvalidOperationException("Mark this box IMAGE before testing image matching.");
        }

        List<VisualAnchorSample> samples = [];
        GrayImage? activeImage = null;
        foreach (DatasetFrame frame in dataset.Manifest.Frames)
        {
            OcrTextRegion? region = FindVisualRegion(frame, selectedTrial, label);
            if (region is null) continue;
            GrayImage image = LoadGray(Path.Combine(dataset.ImagesPath, frame.FileName));
            samples.Add(new VisualAnchorSample(image, region.Bounds));
            if (ReferenceEquals(frame, activeFrame) || frame.FileName == activeFrame.FileName) activeImage = image;
        }

        if (samples.Count < 3)
        {
            throw new InvalidOperationException(
                $"Need at least 3 frames with a box labeled '{label}'. Found {samples.Count}.");
        }

        activeImage ??= LoadGray(Path.Combine(dataset.ImagesPath, activeFrame.FileName));
        VisualAnchorDefinition definition = new(ToAnchorId(label), [label]);
        Stopwatch timer = Stopwatch.StartNew();
        VisualAnchorProfile profile = new VisualFingerprintBuilder().Build(
            definition, samples, DateTimeOffset.UtcNow);
        long buildMilliseconds = timer.ElapsedMilliseconds;
        timer.Restart();
        VisualAnchorMatchResult match = new VisualAnchorMatcher().Match(activeImage, profile, selected.Bounds);
        timer.Stop();
        PixelRect previewBounds = match.Bounds ?? selected.Bounds;
        return new ReviewVisualMatchResult(
            match.Status,
            match.Bounds,
            $"{match.Status.ToString().ToUpperInvariant()}  {match.Score:P1}",
            $"gray {match.GrayScore:P1}  ·  edge {match.EdgeScore:P1}  ·  margin {match.DistinctMargin:P1}",
            $"{samples.Count} samples  ·  {profile.Strategy}  ·  phase {FormatPhase(match.PhaseIndex)}",
            $"build {buildMilliseconds} ms  ·  match {timer.ElapsedMilliseconds} ms  ·  {match.CandidateCount} candidates",
            $"expected {FormatBounds(selected.Bounds)}  ·  result {FormatBounds(match.Bounds)}",
            CreateBitmap(profile.MedianTemplate),
            CreateBitmap(profile.GrayReliability),
            CreateBitmap(Crop(activeImage, previewBounds)));
    }

    private static OcrTextRegion? FindVisualRegion(DatasetFrame frame, OcrTrial selectedTrial, string label) =>
        frame.Annotations
            .Select(annotation => annotation.OcrTrials
                .Where(trial => trial.ModelName == selectedTrial.ModelName && trial.Device == selectedTrial.Device)
                .OrderByDescending(trial => trial.RanAtUtc)
                .FirstOrDefault())
            .Where(trial => trial is not null)
            .SelectMany(trial => trial!.Regions)
            .Where(region => region.IsVisualAnchor && SameText(region.Text, label))
            .OrderByDescending(region => region.RecognitionConfidence)
            .FirstOrDefault();

    private static bool SameText(string first, string second) => NormalizeText(first) == NormalizeText(second);

    private static string NormalizeText(string value) => new(value
        .Where(char.IsAsciiLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static GrayImage LoadGray(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame source = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        FormatConvertedBitmap converted = new(source, PixelFormats.Rgb24, null, 0);
        int stride = checked(converted.PixelWidth * 3);
        byte[] pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        return RgbGrayConverter.Convert(new RgbImage(converted.PixelWidth, converted.PixelHeight, pixels, true));
    }

    private static GrayImage Crop(GrayImage source, PixelRect bounds)
    {
        byte[] pixels = new byte[checked(bounds.Width * bounds.Height)];
        for (int y = 0; y < bounds.Height; y++)
        {
            for (int x = 0; x < bounds.Width; x++)
            {
                pixels[y * bounds.Width + x] = source[bounds.X + x, bounds.Y + y];
            }
        }
        return new GrayImage(bounds.Width, bounds.Height, pixels);
    }

    private static BitmapSource CreateBitmap(GrayImage image)
    {
        BitmapSource bitmap = BitmapSource.Create(
            image.Width, image.Height, 96, 96, PixelFormats.Gray8, null,
            image.Pixels.ToArray(), image.Width);
        bitmap.Freeze();
        return bitmap;
    }

    private static string ToAnchorId(string label)
    {
        string id = new string(label.ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrEmpty(id) ? "review-anchor" : id[..Math.Min(id.Length, 128)];
    }

    private static string FormatBounds(PixelRect? bounds) => bounds is { } box
        ? $"[{box.X},{box.Y},{box.Width},{box.Height}]"
        : "none";

    private static string FormatPhase(int phase) => phase < 0 ? "median" : (phase + 1).ToString();
}

internal sealed record ReviewVisualMatchResult(
    VisualAnchorMatchStatus Status,
    PixelRect? Bounds,
    string Summary,
    string Scores,
    string Profile,
    string Timings,
    string Coordinates,
    ImageSource MedianImage,
    ImageSource ReliabilityImage,
    ImageSource MatchedImage);
