namespace LilacMacro.App.Debugging;

internal enum DebugEvidenceMode
{
    Ocr,
    ImageWithOcrFallback,
}

internal enum DebugEvidenceExecutionPlan
{
    OcrOnly,
    ImageThenOcrFallback,
    OcrForLiveBounds,
}

internal static class DebugEvidenceModePolicy
{
    public static DebugEvidenceExecutionPlan Select(
        DebugEvidenceMode mode,
        bool canUseImageWithoutLiveBounds) => mode switch
        {
            DebugEvidenceMode.Ocr => DebugEvidenceExecutionPlan.OcrOnly,
            DebugEvidenceMode.ImageWithOcrFallback when canUseImageWithoutLiveBounds =>
                DebugEvidenceExecutionPlan.ImageThenOcrFallback,
            DebugEvidenceMode.ImageWithOcrFallback => DebugEvidenceExecutionPlan.OcrForLiveBounds,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
}
