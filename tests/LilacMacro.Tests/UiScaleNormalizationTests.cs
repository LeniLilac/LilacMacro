using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.Runtime.Normalization;

namespace LilacMacro.Tests;

public sealed class UiScaleNormalizationTests
{
    [Theory]
    [InlineData(1.00, 1.10, 0.91)]
    [InlineData(1.00, 0.90, 1.11)]
    [InlineData(0.91, 1.02, 0.89)]
    [InlineData(1.00, 1.40, 0.80)]
    [InlineData(1.00, 0.70, 1.20)]
    public void FeedbackPolicy_UsesBoundedReciprocalRenderedCorrection(
        double applied,
        double observed,
        double expected)
    {
        Assert.Equal(expected, UiScaleFeedbackPolicy.Correct(applied, observed));
    }

    [Fact]
    public void FeedbackPolicy_FormatsInputWithoutInspectingDisplayedText()
    {
        Assert.Equal("0.91", UiScaleFeedbackPolicy.Format(0.91));
    }

    [Fact]
    public void FeedbackPolicy_RejectsInvalidMeasurements()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UiScaleFeedbackPolicy.Correct(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UiScaleFeedbackPolicy.Correct(1, double.NaN));
    }

    [Fact]
    public async Task CalibrationStore_IsolatedByWindowsSessionAndRejectsStaleSchema()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            UiScaleCalibrationStore firstSession = new(root, 4);
            UiScaleCalibrationStore secondSession = new(root, 8);

            await firstSession.SaveAsync(0.91);

            Assert.Equal(0.91, await firstSession.LoadAsync());
            Assert.Null(await secondSession.LoadAsync());

            await File.WriteAllTextAsync(
                Path.Combine(root, "ui-scale-calibration.json"),
                """{"version":2,"sessions":{"windows-session-4":{"value":1.2}}}""");
            Assert.Null(await firstSession.LoadAsync());

            await File.WriteAllTextAsync(
                Path.Combine(root, "ui-scale-calibration.json"),
                """{"version":1,"sessions":{"windows-session-4":{"value":1.5}}}""");
            Assert.Null(await firstSession.LoadAsync());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Theory]
    [InlineData(0.80)]
    [InlineData(1.00)]
    [InlineData(1.20)]
    public void PanelDetector_RecoversSupportedRenderedScale(double scale)
    {
        RgbImage image = PanelImage(scale);

        UiScalePanelMatch match = UiScalePanelDetector.DetectPanel(image);

        Assert.True(match.Visible);
        Assert.True(match.Settled);
        Assert.InRange(match.RenderedScale, scale - 0.01, scale + 0.01);
    }

    [Fact]
    public void PanelDetector_RejectsCloseControlWithoutIndependentBorders()
    {
        RgbImage image = BlankImage();
        DrawClose(image, 1.00);

        Assert.False(UiScalePanelDetector.DetectPanel(image).Visible);
    }

    [Fact]
    public void GearDetector_AcceptsFilledAndOutlinedGlyphsOnDarkFixedButton()
    {
        RgbImage filled = BlankImage();
        Fill(filled, new PixelRect(210, 12, 41, 44), 25, 28, 32);
        Fill(filled, new PixelRect(221, 25, 18, 18), 235, 235, 235);
        Fill(filled, new PixelRect(226, 30, 8, 8), 25, 28, 32);
        Assert.Equal(new PixelPoint(230, 34), UiScalePanelDetector.DetectSettingsGear(filled));

        RgbImage outlined = BlankImage();
        Fill(outlined, new PixelRect(210, 12, 41, 44), 25, 28, 32);
        Fill(outlined, new PixelRect(221, 24, 20, 2), 235, 235, 235);
        Fill(outlined, new PixelRect(221, 42, 20, 2), 235, 235, 235);
        Fill(outlined, new PixelRect(221, 26, 2, 16), 235, 235, 235);
        Fill(outlined, new PixelRect(239, 26, 2, 16), 235, 235, 235);
        Assert.Equal(new PixelPoint(230, 34), UiScalePanelDetector.DetectSettingsGear(outlined));
    }

    [Fact]
    public void GearDetector_RejectsMissingButtonAndIncompleteGlyph()
    {
        RgbImage missingButton = BlankImage();
        Fill(missingButton, new PixelRect(210, 12, 41, 44), 120, 120, 120);
        Fill(missingButton, new PixelRect(221, 24, 20, 20), 235, 235, 235);
        Assert.Null(UiScalePanelDetector.DetectSettingsGear(missingButton));

        RgbImage incompleteGlyph = BlankImage();
        Fill(incompleteGlyph, new PixelRect(210, 12, 41, 44), 25, 28, 32);
        Fill(incompleteGlyph, new PixelRect(221, 24, 20, 4), 235, 235, 235);
        Assert.Null(UiScalePanelDetector.DetectSettingsGear(incompleteGlyph));
    }

    [Fact]
    public void OcrPolicy_RequiresSettingsStructureAndFindsScaleValue()
    {
        UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(PanelImage(1.00));
        OcrTextRegion[] regions =
        [
            Region("Settings", 240, 100, 100, 30),
            Region("UI Scale", 520, 115, 70, 20),
            Region("All", 255, 155, 40, 20),
            Region("Audio", 255, 200, 60, 20),
            Region("Gameplay", 255, 245, 90, 20),
            Region("Miscellaneous", 440, 160, 120, 20),
            Region("UI Scale", 445, 200, 75, 20),
            Region("Adjust the size of all UI", 445, 220, 190, 20),
            Region("elements", 445, 240, 70, 18),
            Region("1", 610, 202, 14, 18),
        ];

        SettingsSearchEvidence? search = UiScaleOcrPolicy.FindSettingsSearch(regions, panel);
        UiScaleRowEvidence? row = UiScaleOcrPolicy.FindUiScaleRow(regions, panel);

        Assert.NotNull(search);
        Assert.NotNull(row);
        Assert.Equal(new PixelPoint(618, 213), row!.ValuePoint);
    }

    [Fact]
    public void OcrPolicy_DoesNotRequireOrReadNumericScaleText()
    {
        UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(PanelImage(1.00));
        OcrTextRegion[] regions =
        [
            Region("Settings", 240, 100, 100, 30),
            Region("Miscellaneous", 440, 160, 120, 20),
            Region("UI Scale", 445, 200, 75, 20),
            Region("Adjust the size of all UI", 445, 220, 190, 20),
            Region("elements", 445, 240, 70, 18),
            Region("not a scale value", 610, 202, 100, 18),
        ];

        UiScaleRowEvidence? row = UiScaleOcrPolicy.FindUiScaleRow(regions, panel);
        Assert.NotNull(row);
        Assert.Equal(new PixelPoint(618, 213), row!.ValuePoint);
    }

    [Fact]
    public void SuppliedScaleDataset_AllPanelsAreStableAndMonotonic()
    {
        string? root = Environment.GetEnvironmentVariable("LILACMACRO_UI_SCALE_DATASET");
        if (string.IsNullOrWhiteSpace(root)) return;

        string[] paths = Directory.GetFiles(Path.GetFullPath(root), "frame-*.png")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(40, paths.Length);
        double previous = 0;
        foreach (string path in paths)
        {
            RgbImage image = LoadPng(path);
            UiScalePanelMatch match = UiScalePanelDetector.DetectPanel(image);
            Assert.True(match.Visible, $"{path} | {DescribeDetector(image)}");
            Assert.True(match.Settled, path);
            Assert.True(match.RenderedScale >= previous - 0.006, path);
            Assert.NotNull(UiScalePanelDetector.DetectSettingsGear(image));
            previous = match.RenderedScale;
        }
        Assert.InRange(UiScalePanelDetector.DetectPanel(LoadPng(paths[0])).RenderedScale, 0.79, 0.81);
        Assert.InRange(UiScalePanelDetector.DetectPanel(LoadPng(paths[^1])).RenderedScale, 1.19, 1.21);
    }

    [Fact]
    public void SuppliedLobbyDataset_ExposesGearWithoutFalsePanelOwnership()
    {
        string? root = Environment.GetEnvironmentVariable("LILACMACRO_UI_SCALE_LOBBY_DATASET");
        if (string.IsNullOrWhiteSpace(root)) return;

        foreach (string path in Directory.GetFiles(Path.Combine(Path.GetFullPath(root), "images"), "frame-*.png"))
        {
            RgbImage image = LoadPng(path);
            Assert.NotNull(UiScalePanelDetector.DetectSettingsGear(image));
            UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(image);
            Assert.False(panel.Visible, panel.Visible ? $"{path} | {DescribeDetector(image)}" : path);
        }
    }

    [Fact]
    public async Task SuppliedAnnotatedDataset_ProvidesSearchAndScaleRowEvidence()
    {
        string? root = Environment.GetEnvironmentVariable("LILACMACRO_UI_SCALE_ANNOTATED_DATASET");
        if (string.IsNullOrWhiteSpace(root)) return;

        DatasetLocation dataset = await new DatasetStore().LoadAsync(Path.GetFullPath(root));
        DatasetFrame frame = dataset.Manifest.Frames[0];
        UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(
            LoadPng(Path.Combine(dataset.DirectoryPath, dataset.Manifest.ImageRoot, frame.FileName)));
        OcrTextRegion[] regions = frame.Annotations
            .SelectMany(annotation => annotation.OcrTrials)
            .SelectMany(trial => trial.Regions)
            .ToArray();

        SettingsSearchEvidence? search = UiScaleOcrPolicy.FindSettingsSearch(regions, panel);
        UiScaleRowEvidence? row = UiScaleOcrPolicy.FindUiScaleRow(regions, panel);

        Assert.NotNull(search);
        Assert.NotNull(row);
        Assert.Equal(new PixelPoint(605, 186), row!.ValuePoint);
    }

    [Fact]
    public async Task SuppliedScaleFrames_LiveOcrFindsValuesAcrossSupportedRange()
    {
        string? root = Environment.GetEnvironmentVariable("LILACMACRO_UI_SCALE_LIVE_OCR");
        if (string.IsNullOrWhiteSpace(root)) return;

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        DeepDebugSessionService deepDebug = new(temporaryRoot);
        using OcrRunner ocr = new(deepDebug) { KeepLoaded = true };
        string device = ocr.IsDeviceReady(OcrRunner.GpuDevice) ? OcrRunner.GpuDevice : OcrRunner.CpuDevice;
        await ocr.WarmUpAsync(OcrRunner.SmallModel, device);
        try
        {
            foreach (int frame in new[] { 1, 20, 40 })
            {
                string path = Path.Combine(Path.GetFullPath(root), $"frame-{frame:D4}.png");
                UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(LoadPng(path));
                OcrWorkerResult result = await ocr.RunAsync(
                    path,
                    panel.PanelBounds,
                    OcrRunner.SmallModel,
                    device);
                OcrTextRegion[] regions = result.Regions.Select(candidate => Region(
                    candidate.Text,
                    candidate.X,
                    candidate.Y,
                    candidate.Width,
                    candidate.Height)).ToArray();

                Assert.NotNull(UiScaleOcrPolicy.FindSettingsSearch(regions, panel));
                UiScaleRowEvidence? row = UiScaleOcrPolicy.FindUiScaleRow(regions, panel);
                Assert.True(row is not null, $"Frame {frame}: {result.Text}");
            }
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static RgbImage PanelImage(double scale)
    {
        RgbImage image = BlankImage();
        int left = (int)Math.Round(683 - 447 * scale);
        int top = (int)Math.Round(350 - 253 * scale);
        int right = (int)Math.Round(683 + 447 * scale);
        int bottom = (int)Math.Round(350 + 253 * scale);
        Fill(image, new PixelRect(left - 2, top + 40, 5, bottom - top - 50), 0, 150, 210);
        Fill(image, new PixelRect(right - 3, top + 40, 5, bottom - top - 50), 0, 150, 210);
        Fill(image, new PixelRect(left + 100, bottom - 3, right - left - 110, 5), 0, 150, 210);
        DrawClose(image, scale);
        return image;
    }

    private static void DrawClose(RgbImage image, double scale)
    {
        int centerX = (int)Math.Round(683 + 430 * scale);
        int centerY = (int)Math.Round(350 - 237.5 * scale);
        int side = (int)Math.Round(32 * scale);
        Fill(image, new PixelRect(centerX - side / 2, centerY - side / 2, side, side), 210, 25, 30);
    }

    private static RgbImage BlankImage() => new(1366, 700, new byte[1366 * 700 * 3], takeOwnership: true);

    private static void Fill(RgbImage image, PixelRect region, byte red, byte green, byte blue)
    {
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X; x < region.Right; x++)
            {
                int pixel = (y * image.Size.Width + x) * 3;
                image.Pixels[pixel] = red;
                image.Pixels[pixel + 1] = green;
                image.Pixels[pixel + 2] = blue;
            }
    }

    private static OcrTextRegion Region(string text, int x, int y, int width, int height) => new()
    {
        Bounds = new PixelRect(x, y, width, height),
        Text = text,
        RecognitionConfidence = 0.99,
    };

    private static RgbImage LoadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame source = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        FormatConvertedBitmap converted = new(source, PixelFormats.Rgb24, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 3];
        converted.CopyPixels(pixels, converted.PixelWidth * 3, 0);
        return new RgbImage(converted.PixelWidth, converted.PixelHeight, pixels, takeOwnership: true);
    }

    private static string DescribeDetector(RgbImage image)
    {
        System.Reflection.MethodInfo method = typeof(UiScalePanelDetector).GetMethod(
            "FindRedComponents",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        System.Collections.IEnumerable components = (System.Collections.IEnumerable)method.Invoke(null, [image])!;
        object[] found = components.Cast<object>().ToArray();
        object close = found.OrderByDescending(component => (int)component.GetType().GetProperty("Count")!.GetValue(component)!).First();
        int minX = (int)close.GetType().GetProperty("MinimumX")!.GetValue(close)!;
        int maxX = (int)close.GetType().GetProperty("MaximumX")!.GetValue(close)!;
        int minY = (int)close.GetType().GetProperty("MinimumY")!.GetValue(close)!;
        int maxY = (int)close.GetType().GetProperty("MaximumY")!.GetValue(close)!;
        double scale = ((((minX + maxX) / 2d) - 683) / 430 + (350 - (minY + maxY) / 2d) / 237.5) / 2;
        int left = (int)Math.Round(683 - 447 * scale);
        int top = (int)Math.Round(350 - 253 * scale);
        int right = (int)Math.Round(683 + 447 * scale);
        int bottom = (int)Math.Round(350 + 253 * scale);
        System.Reflection.MethodInfo cyan = typeof(UiScalePanelDetector).GetMethod(
            "CyanFraction",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        double l = (double)cyan.Invoke(null, [image, new PixelRect(left - 2, top + 40, 5, bottom - top - 50)])!;
        double r = (double)cyan.Invoke(null, [image, new PixelRect(right - 3, top + 40, 5, bottom - top - 50)])!;
        double b = (double)cyan.Invoke(null, [image, new PixelRect(left + 100, bottom - 3, right - left - 110, 5)])!;
        return $"scale={scale:0.0000}, borders={l:0.000}/{r:0.000}/{b:0.000} | {string.Join(" | ", found.AsEnumerable())}";
    }
}
