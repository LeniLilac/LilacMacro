using LilacMacro.Core.Geometry;
using LilacMacro.Core.Vision;

namespace LilacMacro.Tests;

public sealed class VisualAnchorTests
{
    [Fact]
    public void BuilderAndMatcher_HandleAnimatedBackgroundWithoutElementSpecificPolicy()
    {
        PixelRect bounds = new(10, 8, 32, 20);
        VisualAnchorSample[] samples = Enumerable.Range(0, 8)
            .Select(index => new VisualAnchorSample(CreateFrame(bounds, index, 0, 0), bounds))
            .ToArray();
        VisualAnchorProfile profile = new VisualFingerprintBuilder().Build(
            new VisualAnchorDefinition("lobby.generic", ["generic"]),
            samples,
            DateTimeOffset.UnixEpoch);

        Assert.NotEqual(VisualAnchorStrategy.OcrOnly, profile.Strategy);
        Assert.True(profile.Metrics.DynamicPixelRatio > 0.08);

        PixelRect shifted = new(15, 11, bounds.Width, bounds.Height);
        GrayImage current = CreateFrame(bounds, 13, 5, 3);
        VisualAnchorMatchResult result = new VisualAnchorMatcher().Match(
            current,
            profile,
            bounds,
            new VisualAnchorMatcherOptions
            {
                HorizontalSearchRadius = 8,
                VerticalSearchRadius = 6,
                SearchStep = 1,
                ScaleFactors = [1],
                MinimumScore = 0.70,
            });

        Assert.True(result.IsMatch);
        Assert.Equal(shifted, result.Bounds);
        Assert.True(result.EdgeScore > 0.90);
    }

    [Fact]
    public void Builder_SelectsOcrOnlyForStructurelessAnimatedCrop()
    {
        PixelRect bounds = new(0, 0, 16, 16);
        VisualAnchorSample[] samples = Enumerable.Range(0, 6)
            .Select(index => new VisualAnchorSample(
                new GrayImage(16, 16, Enumerable.Repeat((byte)(index * 40), 256).ToArray()),
                bounds))
            .ToArray();

        VisualAnchorProfile profile = new VisualFingerprintBuilder().Build(
            new VisualAnchorDefinition("unstable.uniform", ["uniform"]),
            samples,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VisualAnchorStrategy.OcrOnly, profile.Strategy);
        VisualAnchorMatchResult result = new VisualAnchorMatcher().Match(samples[0].Frame, profile, bounds);
        Assert.Equal(VisualAnchorMatchStatus.RequiresOcr, result.Status);
    }

    [Fact]
    public async Task Store_RoundTripsVersionedInspectableAssets()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro-visual-profile-tests", Guid.NewGuid().ToString("N"));
        try
        {
            PixelRect bounds = new(4, 4, 24, 16);
            VisualAnchorSample[] samples = Enumerable.Range(0, 4)
                .Select(index => new VisualAnchorSample(CreateFrame(bounds, index, 0, 0), bounds))
                .ToArray();
            VisualAnchorProfile expected = new VisualFingerprintBuilder().Build(
                new VisualAnchorDefinition("lobby.events", ["events"]),
                samples,
                DateTimeOffset.Parse("2026-08-05T12:00:00Z"));
            VisualProfileStore store = new();

            string revision = await store.SaveRevisionAsync(root, expected);
            VisualAnchorProfile actual = await store.LoadCurrentAsync(root, expected.Definition.Id);

            Assert.True(File.Exists(Path.Combine(revision, VisualProfileStore.ManifestFileName)));
            Assert.True(File.Exists(Path.Combine(revision, "median.pgm")));
            Assert.Equal(expected.RevisionId, actual.RevisionId);
            Assert.Equal(expected.Strategy, actual.Strategy);
            Assert.Equal(expected.MedianTemplate.Pixels.ToArray(), actual.MedianTemplate.Pixels.ToArray());
            Assert.Equal(expected.Definition.TextAliases, actual.Definition.TextAliases);
            await Assert.ThrowsAsync<IOException>(() => store.SaveRevisionAsync(root, expected));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Store_RejectsModifiedRaster()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro-visual-profile-tests", Guid.NewGuid().ToString("N"));
        try
        {
            PixelRect bounds = new(4, 4, 24, 16);
            VisualAnchorSample[] samples = Enumerable.Range(0, 4)
                .Select(index => new VisualAnchorSample(CreateFrame(bounds, index, 0, 0), bounds))
                .ToArray();
            VisualAnchorProfile profile = new VisualFingerprintBuilder().Build(
                new VisualAnchorDefinition("tamper.test", ["test"]),
                samples,
                DateTimeOffset.UnixEpoch);
            VisualProfileStore store = new();
            string revision = await store.SaveRevisionAsync(root, profile);
            await File.AppendAllTextAsync(Path.Combine(revision, "median.pgm"), "modified");

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadRevisionAsync(revision));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StateRule_UsesDeclarativeDistinctAnchorEvidence()
    {
        VisualStateRule rule = new("Lobby", 2, ["lobby.store", "lobby.play", "lobby.events"]);
        VisualStateEvaluation result = VisualStateRuleEngine.Evaluate(
            rule,
            [
                new("lobby.store", VisualAnchorMatchStatus.Matched, 0.94),
                new("lobby.play", VisualAnchorMatchStatus.Matched, 0.91),
                new("lobby.events", VisualAnchorMatchStatus.RequiresOcr, 0),
            ]);

        Assert.True(result.IsMatch);
        Assert.Equal(2, result.Matches.Count);
        Assert.Single(result.Uncertain);
    }

    [Fact]
    public void StateRule_RejectsDuplicateObservations()
    {
        VisualStateRule rule = new("Lobby", 1, ["lobby.play"]);
        VisualAnchorObservation observation = new("lobby.play", VisualAnchorMatchStatus.Matched, 0.9);

        Assert.Throws<ArgumentException>(() => VisualStateRuleEngine.Evaluate(rule, [observation, observation]));
    }

    [Fact]
    public void Builder_RejectsTooFewSamples()
    {
        PixelRect bounds = new(4, 4, 24, 16);
        VisualAnchorSample[] samples =
        [
            new(CreateFrame(bounds, 0, 0, 0), bounds),
            new(CreateFrame(bounds, 1, 0, 0), bounds),
        ];

        Assert.Throws<ArgumentException>(() => new VisualFingerprintBuilder().Build(
            new VisualAnchorDefinition("sample.boundary", ["sample"]),
            samples,
            DateTimeOffset.UnixEpoch));
    }

    private static GrayImage CreateFrame(PixelRect original, int phase, int offsetX, int offsetY)
    {
        const int width = 64;
        const int height = 40;
        byte[] pixels = Enumerable.Repeat((byte)18, width * height).ToArray();
        PixelRect bounds = new(original.X + offsetX, original.Y + offsetY, original.Width, original.Height);
        for (int y = 0; y < bounds.Height; y++)
        {
            for (int x = 0; x < bounds.Width; x++)
            {
                int animated = 45 + ((x * 7 + y * 3 + phase * 29) % 115);
                pixels[(bounds.Y + y) * width + bounds.X + x] = (byte)animated;
            }
        }

        DrawRect(pixels, width, bounds.X + 3, bounds.Y + 3, 4, 14, 238);
        DrawRect(pixels, width, bounds.X + 3, bounds.Y + 3, 18, 4, 238);
        DrawRect(pixels, width, bounds.X + 3, bounds.Y + 9, 14, 3, 238);
        DrawRect(pixels, width, bounds.X + 18, bounds.Y + 3, 3, 14, 238);
        return new GrayImage(width, height, pixels);
    }

    private static void DrawRect(byte[] pixels, int stride, int x, int y, int width, int height, byte value)
    {
        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++) pixels[row * stride + column] = value;
        }
    }
}
