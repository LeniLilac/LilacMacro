using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal enum DebugMatchMode
{
    DistinctTargets,
    ExactTargets,
    RequiredFirstTarget,
    DeclarativeEvidence,
    RepeatedTarget,
}

internal sealed record DebugStateSpec(
    string Name,
    string DatasetDirectory,
    IReadOnlyList<int> RegionFrames,
    int RequiredMatches,
    IReadOnlyList<OcrTargetRule> Targets,
    DebugMatchMode MatchMode = DebugMatchMode.DistinctTargets,
    IReadOnlyList<string>? RequiredTargetNames = null,
    IReadOnlyList<string>? PoolTargetNames = null,
    int MinimumPoolMatches = 0,
    IReadOnlyList<string>? FuzzyPrefixTargetNames = null,
    IReadOnlyList<string>? SameRowTargetNames = null,
    string? RegionLabel = null);
