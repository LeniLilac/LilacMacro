using LilacMacro.Core.Capture;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Tests;

public sealed class DatasetStoreTests
{
    [Fact]
    public async Task RoundTrip_PreservesAgentAndOcrMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            DatasetStore store = new();
            CapturePlan plan = new()
            {
                TargetSize = PixelSize.Create(320, 240),
                FrameCount = 1,
                Duration = TimeSpan.Zero,
            };
            DatasetLocation draft = await store.CreateDraftAsync(root, plan, "Roblox", 42, DateTimeOffset.UtcNow);
            byte[] png = PngEncoder.Encode(new RgbImage(320, 240, new byte[320 * 240 * 3], takeOwnership: true));
            DatasetFrame frame = await store.AddFrameAsync(draft, png, 320, 240, DateTimeOffset.UtcNow);
            BoxAnnotation region = new()
            {
                Bounds = new PixelRect(12, 18, 100, 24),
                Label = "wave label",
                Notes = "player-authored note",
            };
            region.OcrTrials.Add(new OcrTrial
            {
                ModelName = "PP-OCRv6_small_rec",
                DetectorModelName = "PP-OCRv6_small_det",
                Device = "gpu:0",
                Text = "Wave 12",
                Confidence = 0.94,
                ModelLoadMilliseconds = 210,
                InferenceMilliseconds = 18,
                RuntimeVersion = "3.7.0",
                RanAtUtc = DateTimeOffset.UtcNow,
                ModelWasCached = true,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Bounds = new PixelRect(18, 20, 72, 18),
                        Text = "Wave 12",
                        DetectionConfidence = 0.91,
                        RecognitionConfidence = 0.94,
                    },
                ],
            });
            frame.Annotations.Add(region);
            frame.Verdict = FrameVerdict.Positive;
            await store.SaveAsync(draft);

            DatasetLocation loaded = await store.LoadAsync(draft.DirectoryPath);
            OcrTrial loadedTrial = Assert.Single(Assert.Single(loaded.Manifest.Frames).Annotations).OcrTrials.Single();
            string manifestJson = await File.ReadAllTextAsync(draft.ManifestPath);

            Assert.Equal("lilacmacro.dataset", loaded.Manifest.Format);
            Assert.Equal("roblox_client_pixels_half_open", loaded.Manifest.CoordinateSpace);
            Assert.Equal(DatasetCaptureMode.Timed, loaded.Manifest.CaptureMode);
            Assert.Equal("Wave 12", loadedTrial.Text);
            Assert.Equal("3.7.0", loadedTrial.RuntimeVersion);
            Assert.Equal("PP-OCRv6_small_det", loadedTrial.DetectorModelName);
            Assert.Equal("gpu:0", loadedTrial.Device);
            Assert.True(loadedTrial.ModelWasCached);
            Assert.Equal(new PixelRect(18, 20, 72, 18), Assert.Single(loadedTrial.Regions).Bounds);
            Assert.DoesNotContain("\"right\"", manifestJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"bottom\"", manifestJson, StringComparison.Ordinal);
            Assert.Contains("\"capture_mode\": \"timed\"", manifestJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ManualDraft_AppendsIndividuallyCapturedFrames()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            DatasetStore store = new();
            DatasetLocation draft = await store.CreateManualDraftAsync(
                root,
                PixelSize.Create(320, 240),
                "Roblox",
                42,
                DateTimeOffset.UtcNow);
            byte[] png = PngEncoder.Encode(new RgbImage(320, 240, new byte[320 * 240 * 3], takeOwnership: true));

            await store.AddFrameAsync(draft, png, 320, 240, DateTimeOffset.UtcNow);
            await store.AddFrameAsync(draft, png, 320, 240, DateTimeOffset.UtcNow.AddSeconds(1));
            DatasetLocation loaded = await store.LoadAsync(draft.DirectoryPath);

            Assert.Equal(DatasetCaptureMode.Manual, loaded.Manifest.CaptureMode);
            Assert.Equal(0, loaded.Manifest.RequestedFrameCount);
            Assert.Equal(0, loaded.Manifest.RequestedDurationSeconds);
            Assert.Collection(
                loaded.Manifest.Frames,
                frame => Assert.Equal("frame-0001.png", frame.FileName),
                frame => Assert.Equal("frame-0002.png", frame.FileName));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateDraft_AllocatesDistinctConsecutiveDatasets()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            DatasetStore store = new();
            CapturePlan plan = new()
            {
                TargetSize = PixelSize.Create(320, 240),
                FrameCount = 1,
                Duration = TimeSpan.Zero,
            };
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;

            DatasetLocation first = await store.CreateDraftAsync(root, plan, "Roblox", 42, timestamp);
            DatasetLocation second = await store.CreateDraftAsync(root, plan, "Roblox", 42, timestamp);

            Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
            Assert.True(File.Exists(first.ManifestPath));
            Assert.True(File.Exists(second.ManifestPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Save_RejectsOcrChildOutsideManualRegion()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            DatasetStore store = new();
            CapturePlan plan = new()
            {
                TargetSize = PixelSize.Create(320, 240),
                FrameCount = 1,
                Duration = TimeSpan.Zero,
            };
            DatasetLocation draft = await store.CreateDraftAsync(root, plan, "Roblox", 42, DateTimeOffset.UtcNow);
            byte[] png = PngEncoder.Encode(new RgbImage(320, 240, new byte[320 * 240 * 3], takeOwnership: true));
            DatasetFrame frame = await store.AddFrameAsync(draft, png, 320, 240, DateTimeOffset.UtcNow);
            BoxAnnotation annotation = new() { Bounds = new PixelRect(10, 10, 20, 20) };
            annotation.OcrTrials.Add(new OcrTrial
            {
                ModelName = "PP-OCRv6_small_rec",
                DetectorModelName = "PP-OCRv6_small_det",
                Device = "cpu",
                Text = "outside",
                Confidence = 0.9,
                ModelLoadMilliseconds = 1,
                InferenceMilliseconds = 1,
                RuntimeVersion = "3.7.0",
                RanAtUtc = DateTimeOffset.UtcNow,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Bounds = new PixelRect(5, 5, 10, 10),
                        Text = "outside",
                        RecognitionConfidence = 0.9,
                    },
                ],
            });
            frame.Annotations.Add(annotation);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(draft));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("gpu:1")]
    [InlineData("cuda")]
    public async Task Save_RejectsUnknownOcrDevice(string device)
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            DatasetStore store = new();
            CapturePlan plan = new()
            {
                TargetSize = PixelSize.Create(320, 240),
                FrameCount = 1,
                Duration = TimeSpan.Zero,
            };
            DatasetLocation draft = await store.CreateDraftAsync(root, plan, "Roblox", 42, DateTimeOffset.UtcNow);
            byte[] png = PngEncoder.Encode(new RgbImage(320, 240, new byte[320 * 240 * 3], takeOwnership: true));
            DatasetFrame frame = await store.AddFrameAsync(draft, png, 320, 240, DateTimeOffset.UtcNow);
            BoxAnnotation annotation = new() { Bounds = new PixelRect(4, 4, 40, 20) };
            annotation.OcrTrials.Add(new OcrTrial
            {
                ModelName = "PP-OCRv6_small_rec",
                DetectorModelName = "PP-OCRv6_small_det",
                Device = device,
                Text = "test",
                Confidence = 0.9,
                ModelLoadMilliseconds = 1,
                InferenceMilliseconds = 1,
                RuntimeVersion = "3.7.0",
                RanAtUtc = DateTimeOffset.UtcNow,
            });
            frame.Annotations.Add(annotation);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(draft));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
