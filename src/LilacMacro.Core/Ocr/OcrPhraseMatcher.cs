using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Ocr;

public static class OcrPhraseMatcher
{
    public const double DefaultFuzzyThreshold = 0.78;
    public const double DefaultFuzzySubstringThreshold = 0.86;
    public const int MinimumFuzzyLength = 8;
    public const int MaximumInputLength = 1024;

    public static OcrPhraseMatchResult Match(
        OcrTextRegion configuredResult,
        string observed,
        double fuzzyThreshold = DefaultFuzzyThreshold)
    {
        ArgumentNullException.ThrowIfNull(configuredResult);
        return Match(configuredResult.Text, observed, configuredResult.MatchMode, fuzzyThreshold);
    }

    public static OcrPhraseMatchResult Match(
        string expected,
        string observed,
        OcrMatchMode mode,
        double fuzzyThreshold = DefaultFuzzyThreshold)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        if (expected.Length > MaximumInputLength || observed.Length > MaximumInputLength)
        {
            throw new ArgumentOutOfRangeException(nameof(expected), $"OCR phrases cannot exceed {MaximumInputLength} characters.");
        }
        if (!double.IsFinite(fuzzyThreshold) || fuzzyThreshold is < 0.5 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fuzzyThreshold));
        }

        string expectedNormalized = Normalize(expected);
        string observedNormalized = Normalize(observed);
        bool exact = expectedNormalized == observedNormalized && expectedNormalized.Length > 0;
        if (mode == OcrMatchMode.Exact || exact ||
            expectedNormalized.Length < MinimumFuzzyLength || observedNormalized.Length < MinimumFuzzyLength)
        {
            return new OcrPhraseMatchResult(exact, exact ? 1 : 0, expectedNormalized, observedNormalized);
        }

        int longest = Math.Max(expectedNormalized.Length, observedNormalized.Length);
        int distance = LevenshteinDistance(expectedNormalized, observedNormalized);
        double similarity = 1 - distance / (double)longest;
        return new OcrPhraseMatchResult(
            similarity >= fuzzyThreshold,
            similarity,
            expectedNormalized,
            observedNormalized);
    }

    public static OcrPhraseMatchResult MatchSubstring(
        string expected,
        string observed,
        double fuzzyThreshold = DefaultFuzzySubstringThreshold)
    {
        string expectedNormalized = Normalize(expected);
        string observedNormalized = Normalize(observed);
        if (expectedNormalized.Length < MinimumFuzzyLength || observedNormalized.Length < MinimumFuzzyLength)
            return Match(expected, observed, OcrMatchMode.FuzzyPhrase, fuzzyThreshold);
        if (observedNormalized.Contains(expectedNormalized, StringComparison.Ordinal))
            return new OcrPhraseMatchResult(true, 1, expectedNormalized, observedNormalized);

        int minimum = Math.Max(MinimumFuzzyLength, expectedNormalized.Length - 2);
        int maximum = Math.Min(observedNormalized.Length, expectedNormalized.Length + 2);
        OcrPhraseMatchResult best = new(false, 0, expectedNormalized, observedNormalized);
        for (int length = minimum; length <= maximum; length++)
        {
            for (int start = 0; start + length <= observedNormalized.Length; start++)
            {
                string candidate = observedNormalized.Substring(start, length);
                OcrPhraseMatchResult match = Match(
                    expectedNormalized,
                    candidate,
                    OcrMatchMode.FuzzyPhrase,
                    fuzzyThreshold);
                if (match.Similarity > best.Similarity)
                    best = match with { ObservedNormalized = observedNormalized };
            }
        }
        return best;
    }

    public static OcrPhraseMatchResult MatchPrefix(
        string expected,
        string observed,
        double fuzzyThreshold = DefaultFuzzyThreshold)
    {
        string expectedNormalized = Normalize(expected);
        string observedNormalized = Normalize(observed);
        int minimum = Math.Max(MinimumFuzzyLength, expectedNormalized.Length - 2);
        int maximum = Math.Min(observedNormalized.Length, expectedNormalized.Length + 2);
        OcrPhraseMatchResult best = new(false, 0, expectedNormalized, observedNormalized);
        for (int length = minimum; length <= maximum; length++)
        {
            OcrPhraseMatchResult match = Match(
                expectedNormalized,
                observedNormalized[..length],
                OcrMatchMode.FuzzyPhrase,
                fuzzyThreshold);
            if (match.Similarity > best.Similarity)
                best = match with { ObservedNormalized = observedNormalized };
        }
        return best;
    }

    public static string Normalize(string value) => new(value
        .Where(character => char.IsAsciiLetterOrDigit(character))
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static int LevenshteinDistance(string first, string second)
    {
        if (first.Length > second.Length) (first, second) = (second, first);
        int[] previous = Enumerable.Range(0, first.Length + 1).ToArray();
        int[] current = new int[first.Length + 1];
        for (int secondIndex = 1; secondIndex <= second.Length; secondIndex++)
        {
            current[0] = secondIndex;
            for (int firstIndex = 1; firstIndex <= first.Length; firstIndex++)
            {
                int substitution = previous[firstIndex - 1] +
                    (first[firstIndex - 1] == second[secondIndex - 1] ? 0 : 1);
                current[firstIndex] = Math.Min(
                    Math.Min(previous[firstIndex] + 1, current[firstIndex - 1] + 1),
                    substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[first.Length];
    }
}

public sealed record OcrPhraseMatchResult(
    bool IsMatch,
    double Similarity,
    string ExpectedNormalized,
    string ObservedNormalized);
