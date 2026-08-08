using LilacMacro.App.Debugging;

namespace LilacMacro.Tests;

public sealed class DebugEvidenceModePolicyTests
{
    [Fact]
    public void Select_OcrModeAlwaysRunsOcr()
    {
        DebugEvidenceExecutionPlan result = DebugEvidenceModePolicy.Select(
            DebugEvidenceMode.Ocr,
            canUseImageWithoutLiveBounds: true);

        Assert.Equal(DebugEvidenceExecutionPlan.OcrOnly, result);
    }

    [Fact]
    public void Select_ImageModeForStateCheckTriesImageBeforeOcrFallback()
    {
        DebugEvidenceExecutionPlan result = DebugEvidenceModePolicy.Select(
            DebugEvidenceMode.ImageWithOcrFallback,
            canUseImageWithoutLiveBounds: true);

        Assert.Equal(DebugEvidenceExecutionPlan.ImageThenOcrFallback, result);
    }

    [Fact]
    public void Select_ImageModeForClickActionRequiresFreshOcrBounds()
    {
        DebugEvidenceExecutionPlan result = DebugEvidenceModePolicy.Select(
            DebugEvidenceMode.ImageWithOcrFallback,
            canUseImageWithoutLiveBounds: false);

        Assert.Equal(DebugEvidenceExecutionPlan.OcrForLiveBounds, result);
    }
}
