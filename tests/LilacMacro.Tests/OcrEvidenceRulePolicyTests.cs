using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class OcrEvidenceRulePolicyTests
{
    [Fact]
    public void IsValid_AcceptsRequiredPlusOneOfPool()
    {
        OcrTextRegion[] regions =
        [
            Region("Teams", OcrEvidenceRole.Required),
            Region("Unequip", OcrEvidenceRole.Pool),
            Region("Unequip all", OcrEvidenceRole.Pool),
            Region("Quick", OcrEvidenceRole.Pool),
            Region("Quick sell", OcrEvidenceRole.Pool),
        ];

        Assert.True(OcrEvidenceRulePolicy.IsValid(1, regions));
    }

    [Fact]
    public void IsValid_RejectsPhraseAssignedToRequiredAndPool()
    {
        OcrTextRegion[] regions =
        [
            Region("Teams", OcrEvidenceRole.Required),
            Region("T e a m s", OcrEvidenceRole.Pool),
        ];

        Assert.False(OcrEvidenceRulePolicy.IsValid(1, regions));
    }

    [Fact]
    public void ClampMinimumPoolMatches_UsesDistinctNormalizedPhrases()
    {
        OcrTextRegion[] regions =
        [
            Region("Quick Sell", OcrEvidenceRole.Pool),
            Region("quick-sell", OcrEvidenceRole.Pool),
            Region("Unequip", OcrEvidenceRole.Pool),
        ];

        Assert.Equal(2, OcrEvidenceRulePolicy.ClampMinimumPoolMatches(4, regions));
    }

    private static OcrTextRegion Region(string text, OcrEvidenceRole role) => new()
    {
        Bounds = new PixelRect(0, 0, 10, 10),
        Text = text,
        RecognitionConfidence = 1,
        IsOcrEvidence = true,
        EvidenceRole = role,
    };
}
