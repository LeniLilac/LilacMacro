using LilacMacro.Core.Datasets;

namespace LilacMacro.Core.Ocr;

public static class OcrEvidenceRulePolicy
{
    public static int ClampMinimumPoolMatches(int requested, IEnumerable<OcrTextRegion> regions)
    {
        int poolCount = DistinctPhrases(regions, OcrEvidenceRole.Pool).Count;
        return poolCount == 0 ? 0 : Math.Clamp(requested <= 0 ? 1 : requested, 1, poolCount);
    }

    public static IReadOnlySet<string> DistinctPhrases(
        IEnumerable<OcrTextRegion> regions,
        OcrEvidenceRole role) => regions
        .Where(region => region.EvidenceRole == role)
        .Select(region => OcrPhraseMatcher.Normalize(region.Text))
        .Where(normalized => normalized.Length > 0)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsValid(int minimumPoolMatches, IEnumerable<OcrTextRegion> regions)
    {
        OcrTextRegion[] configured = regions.Where(region => region.EvidenceRole != OcrEvidenceRole.None).ToArray();
        if (configured.Any(region => !region.IsOcrEvidence && !region.IsVisualAnchor)) return false;
        IReadOnlySet<string> required = DistinctPhrases(configured, OcrEvidenceRole.Required);
        IReadOnlySet<string> pool = DistinctPhrases(configured, OcrEvidenceRole.Pool);
        if (required.Overlaps(pool)) return false;
        return pool.Count == 0 ? minimumPoolMatches == 0 : minimumPoolMatches is >= 1 && minimumPoolMatches <= pool.Count;
    }
}
