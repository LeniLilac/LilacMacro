namespace LilacMacro.Core.Vision;

public sealed record VisualAnchorObservation(
    string AnchorId,
    VisualAnchorMatchStatus Status,
    double Score);

public sealed record VisualStateRule(
    string State,
    int RequiredMatches,
    IReadOnlyList<string> AnchorIds)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(State) || AnchorIds.Count == 0 ||
            RequiredMatches < 1 || RequiredMatches > AnchorIds.Count ||
            AnchorIds.Any(string.IsNullOrWhiteSpace) ||
            AnchorIds.Distinct(StringComparer.Ordinal).Count() != AnchorIds.Count)
        {
            throw new ArgumentException("Visual state rule is invalid.");
        }
    }
}

public sealed record VisualStateEvaluation(
    string State,
    int RequiredMatches,
    IReadOnlyList<VisualAnchorObservation> Matches,
    IReadOnlyList<VisualAnchorObservation> Uncertain)
{
    public bool IsMatch => Matches.Count >= RequiredMatches;
}

public static class VisualStateRuleEngine
{
    public static VisualStateEvaluation Evaluate(
        VisualStateRule rule,
        IReadOnlyList<VisualAnchorObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(observations);
        rule.Validate();
        if (observations.GroupBy(observation => observation.AnchorId, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Only one observation per visual anchor is allowed.", nameof(observations));
        }

        HashSet<string> owned = new(rule.AnchorIds, StringComparer.Ordinal);
        VisualAnchorObservation[] matches = observations
            .Where(observation => owned.Contains(observation.AnchorId) &&
                observation.Status == VisualAnchorMatchStatus.Matched)
            .OrderByDescending(observation => observation.Score)
            .ToArray();
        VisualAnchorObservation[] uncertain = observations
            .Where(observation => owned.Contains(observation.AnchorId) &&
                observation.Status is VisualAnchorMatchStatus.Ambiguous or VisualAnchorMatchStatus.RequiresOcr)
            .OrderBy(observation => observation.AnchorId, StringComparer.Ordinal)
            .ToArray();
        return new VisualStateEvaluation(rule.State, rule.RequiredMatches, matches, uncertain);
    }
}
