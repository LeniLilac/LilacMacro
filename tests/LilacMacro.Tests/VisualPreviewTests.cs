using System.Text.Json;
using LilacMacro.App.Debugging;
using LilacMacro.App.Views;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Vision;

namespace LilacMacro.Tests;

public sealed class VisualPreviewTests
{
    [Fact]
    public void AnchorChoicesKeepValueEqualRegionsAsDistinctSelectorItems()
    {
        OcrTextRegion first = Region();
        OcrTextRegion second = Region();

        ReviewVisualAnchorChoice firstChoice = new(first);
        ReviewVisualAnchorChoice secondChoice = new(second);

        Assert.Equal(first, second);
        Assert.NotEqual(firstChoice, secondChoice);
        Assert.Same(first, firstChoice.Region);
        Assert.Same(second, secondChoice.Region);
    }

    [Fact]
    public void ComparisonJsonOmitsPreviewPixels()
    {
        GrayImage preview = new(1, 1, [128]);
        WireVisualComparison comparison = new(
            "Lobby", "Play", "[1,2,3,4]", "[1,2,3,4]", "MATCHED", 0.9,
            10, 20, 30, "STABLE", true, preview, preview, preview);

        string json = JsonSerializer.Serialize(comparison);

        Assert.DoesNotContain("Preview", json, StringComparison.Ordinal);
        Assert.DoesNotContain("128", json, StringComparison.Ordinal);
    }

    private static OcrTextRegion Region() => new()
    {
        Bounds = new PixelRect(10, 20, 30, 10),
        Text = "Play",
        RecognitionConfidence = 0.99,
        IsVisualAnchor = true,
    };
}
