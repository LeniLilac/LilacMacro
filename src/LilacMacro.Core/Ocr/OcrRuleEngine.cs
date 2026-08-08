using System.Text;
using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Ocr;

public static class OcrRuleEngine
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        StringBuilder normalized = new(text.Length);
        foreach (char character in text)
        {
            if (character is >= 'A' and <= 'Z') normalized.Append((char)(character + ('a' - 'A')));
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9') normalized.Append(character);
        }
        return normalized.ToString();
    }

    public static OcrStateEvaluation Evaluate(
        string state,
        int requiredMatches,
        IReadOnlyList<OcrTargetRule> targets,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(regions);
        if (requiredMatches < 1 || requiredMatches > targets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredMatches));
        }

        OcrTargetMatch[] matches = targets
            .Select(target => FindTarget(target, regions))
            .Where(match => match is not null)
            .Cast<OcrTargetMatch>()
            .ToArray();
        return new OcrStateEvaluation(state, requiredMatches, matches);
    }

    public static OcrStateEvaluation EvaluateExact(
        string state,
        int requiredMatches,
        IReadOnlyList<OcrTargetRule> targets,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(regions);
        if (requiredMatches < 1 || requiredMatches > targets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredMatches));
        }

        OcrTargetMatch[] matches = targets
            .Select(target => FindExactTarget(target, regions))
            .Where(match => match is not null)
            .Cast<OcrTargetMatch>()
            .ToArray();
        return new OcrStateEvaluation(state, requiredMatches, matches);
    }

    public static OcrStateEvaluation EvaluateRepeatedTarget(
        string state,
        int requiredMatches,
        OcrTargetRule target,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(regions);
        if (requiredMatches < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredMatches));
        }

        return new OcrStateEvaluation(
            state,
            requiredMatches,
            FindAllTargets(target, regions));
    }

    public static OcrStateEvaluation EvaluateWithRequiredFirstTarget(
        string state,
        int requiredMatches,
        IReadOnlyList<OcrTargetRule> targets,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(regions);
        if (requiredMatches < 1 || requiredMatches > targets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredMatches));
        }

        OcrTargetMatch? required = FindTarget(targets[0], regions);
        OcrTargetMatch[] supporting = targets
            .Skip(1)
            .Select(target => FindTarget(target, regions))
            .Where(match => match is not null)
            .Cast<OcrTargetMatch>()
            .ToArray();
        OcrTargetMatch[] matches = required is null
            ? supporting
            : [required, .. supporting];
        return new OcrStateEvaluation(
            state,
            requiredMatches,
            matches,
            required is not null);
    }

    public static OcrTargetMatch? FindTarget(
        OcrTargetRule target,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(regions);

        foreach (string alias in target.Aliases)
        {
            string expected = Normalize(alias);
            OcrTargetMatch? best = regions
                .Select(region => CreateMatch(target, alias, expected, region))
                .Where(match => match is not null)
                .Cast<OcrTargetMatch>()
                .OrderBy(match => match.NormalizedText == expected ? 0 : 1)
                .ThenBy(match => match.NormalizedText.Length - expected.Length)
                .ThenByDescending(match => match.Region.RecognitionConfidence)
                .ThenBy(match => match.Region.Bounds.Y)
                .ThenBy(match => match.Region.Bounds.X)
                .FirstOrDefault();
            if (best is not null) return best;
        }

        return null;
    }

    public static OcrTargetMatch? FindExactTarget(
        OcrTargetRule target,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(regions);

        foreach (string alias in target.Aliases)
        {
            string expected = Normalize(alias);
            OcrTextRegion? best = regions
                .Where(region => Normalize(region.Text) == expected)
                .OrderByDescending(region => region.RecognitionConfidence)
                .ThenBy(region => region.Bounds.Y)
                .ThenBy(region => region.Bounds.X)
                .FirstOrDefault();
            if (best is not null)
            {
                return new OcrTargetMatch(target.Name, alias, expected, best);
            }
        }

        return null;
    }

    public static IReadOnlyList<OcrTargetMatch> FindAllTargets(
        OcrTargetRule target,
        IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(regions);

        return regions
            .Select(region => FindMatchForRegion(target, region))
            .Where(match => match is not null)
            .Cast<OcrTargetMatch>()
            .GroupBy(match => match.Region.Bounds)
            .Select(group => group
                .OrderByDescending(match => match.Region.RecognitionConfidence)
                .First())
            .OrderBy(match => match.Region.Bounds.Y)
            .ThenBy(match => match.Region.Bounds.X)
            .ToArray();
    }

    public static OcrTargetMatch? FindLeftmostTarget(
        OcrTargetRule target,
        IReadOnlyList<OcrTextRegion> regions) => FindAllTargets(target, regions)
        .OrderBy(match => match.Region.Bounds.X)
        .ThenBy(match => match.Region.Bounds.Y)
        .ThenByDescending(match => match.Region.RecognitionConfidence)
        .FirstOrDefault();

    private static OcrTargetMatch? FindMatchForRegion(
        OcrTargetRule target,
        OcrTextRegion region)
    {
        foreach (string alias in target.Aliases)
        {
            OcrTargetMatch? match = CreateMatch(target, alias, Normalize(alias), region);
            if (match is not null) return match;
        }
        return null;
    }

    private static OcrTargetMatch? CreateMatch(
        OcrTargetRule target,
        string alias,
        string expected,
        OcrTextRegion region)
    {
        string observed = Normalize(region.Text);
        return expected.Length > 0 && observed.Contains(expected, StringComparison.Ordinal)
            ? new OcrTargetMatch(target.Name, alias, observed, region)
            : null;
    }
}
