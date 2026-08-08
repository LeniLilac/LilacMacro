using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Ocr;

public sealed record OcrTargetMatch(
    string Target,
    string Alias,
    string NormalizedText,
    OcrTextRegion Region);
