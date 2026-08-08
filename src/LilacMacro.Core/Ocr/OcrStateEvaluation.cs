namespace LilacMacro.Core.Ocr;

public sealed record OcrStateEvaluation(
    string State,
    int RequiredMatches,
    IReadOnlyList<OcrTargetMatch> Matches,
    bool RequiredEvidenceMatched = true)
{
    public bool IsMatch => RequiredEvidenceMatched && Matches.Count >= RequiredMatches;
}
