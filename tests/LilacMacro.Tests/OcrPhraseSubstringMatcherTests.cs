using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class OcrPhraseSubstringMatcherTests
{
    [Theory]
    [InlineData("Available in", "Available in 06:44:41")]
    [InlineData("Available in", "Availab1e in 00:12")]
    [InlineData("available in", "AVAILABLE 1N 29:30")]
    public void FuzzySubstringAcceptsDynamicSuffixAndMinorOcrErrors(string expected, string observed) =>
        Assert.True(OcrPhraseMatcher.MatchSubstring(expected, observed).IsMatch);

    [Theory]
    [InlineData("Rewards Available")]
    [InlineData("Resets in 06:44")]
    [InlineData("Unavailable")]
    public void FuzzySubstringRejectsDifferentChallengePhrases(string observed) =>
        Assert.False(OcrPhraseMatcher.MatchSubstring("Available in", observed).IsMatch);
}
