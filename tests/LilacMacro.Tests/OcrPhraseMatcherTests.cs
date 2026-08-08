using LilacMacro.Core.Datasets;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class OcrPhraseMatcherTests
{
    private const string ChallengePhrase =
        "You can complete all available challenges of this type before the next reset!";

    [Fact]
    public void Exact_NormalizesCaseSpacesAndSymbolsButRejectsWrongCharacters()
    {
        Assert.True(OcrPhraseMatcher.Match("Start Game", "$ START-game! ", OcrMatchMode.Exact).IsMatch);
        Assert.False(OcrPhraseMatcher.Match("Start Game", "Start Gane", OcrMatchMode.Exact).IsMatch);
    }

    [Fact]
    public void FuzzyPhrase_AcceptsSeveralRecognitionErrorsInLongText()
    {
        OcrPhraseMatchResult result = OcrPhraseMatcher.Match(
            ChallengePhrase,
            "You can cornplete all availabie challenges of this type before the next reset",
            OcrMatchMode.FuzzyPhrase);

        Assert.True(result.IsMatch);
        Assert.InRange(result.Similarity, 0.90, 1);
    }

    [Fact]
    public void FuzzyPhrase_RejectsUnrelatedLongText()
    {
        OcrPhraseMatchResult result = OcrPhraseMatcher.Match(
            ChallengePhrase,
            "Rewards available for the selected expedition map and current party",
            OcrMatchMode.FuzzyPhrase);

        Assert.False(result.IsMatch);
        Assert.True(result.Similarity < OcrPhraseMatcher.DefaultFuzzyThreshold);
    }

    [Fact]
    public void FuzzyPhrase_DoesNotLoosenShortText()
    {
        Assert.False(OcrPhraseMatcher.Match("Daily", "Dai1y", OcrMatchMode.FuzzyPhrase).IsMatch);
    }

    [Fact]
    public void Match_RejectsUnboundedInput()
    {
        string oversized = new('a', OcrPhraseMatcher.MaximumInputLength + 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OcrPhraseMatcher.Match(oversized, "test phrase", OcrMatchMode.FuzzyPhrase));
    }
}
