using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class OcrSpatialRuleInferenceTests
{
    [Fact]
    public void Infer_SelectsLeftmostDuplicateWithinCoarseRegion()
    {
        OcrTextRegion target = Region("School Grounds", 20, 80);
        OcrTextRegion duplicate = Region("school-grounds", 420, 110);

        OcrSpatialSelector result = OcrSpatialRuleInference.Infer(
            target,
            [target, duplicate],
            new PixelRect(0, 0, 700, 500));

        Assert.Equal(OcrSpatialSelector.Leftmost, result);
    }

    [Fact]
    public void Infer_SelectsTopmostDuplicateWithinCoarseRegion()
    {
        OcrTextRegion target = Region("Story", 200, 20);
        OcrTextRegion duplicate = Region("Story", 205, 280);

        OcrSpatialSelector result = OcrSpatialRuleInference.Infer(
            target,
            [target, duplicate],
            new PixelRect(0, 0, 600, 500));

        Assert.Equal(OcrSpatialSelector.Topmost, result);
    }

    [Fact]
    public void Infer_UsesAnyWhenPhraseIsUnique()
    {
        OcrTextRegion target = Region("Select Stage", 200, 20);

        Assert.Equal(
            OcrSpatialSelector.Any,
            OcrSpatialRuleInference.Infer(target, [target], new PixelRect(0, 0, 600, 500)));
    }

    [Fact]
    public void Select_FailsClosedForAmbiguousAnyAndResolvesLeftmost()
    {
        OcrTextRegion left = Region("School Grounds", 20, 80);
        OcrTextRegion right = Region("School Grounds", 420, 80);
        OcrTextRegion configured = Region("School Grounds", 0, 0);

        Assert.Null(OcrSpatialSelectorPolicy.Select(configured, [left, right]));
        configured.SpatialSelector = OcrSpatialSelector.Leftmost;
        Assert.Same(left, OcrSpatialSelectorPolicy.Select(configured, [left, right]));
    }

    [Fact]
    public void Select_UsesNearestNamedAnchor()
    {
        OcrTextRegion near = Region("Select", 80, 80);
        OcrTextRegion far = Region("Select", 500, 80);
        OcrTextRegion anchor = Region("Story", 40, 80);
        OcrTextRegion configured = Region("Select", 0, 0);
        configured.SpatialSelector = OcrSpatialSelector.NearestAnchor;
        configured.SpatialAnchorText = "Story";

        Assert.Same(near, OcrSpatialSelectorPolicy.Select(configured, [near, far, anchor]));
    }

    private static OcrTextRegion Region(string text, int x, int y) => new()
    {
        Bounds = new PixelRect(x, y, 120, 20),
        Text = text,
        RecognitionConfidence = 1,
    };
}
