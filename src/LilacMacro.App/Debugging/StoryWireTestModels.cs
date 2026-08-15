using System.Text.Json.Serialization;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Vision;
using LilacMacro.App.Runtime;

namespace LilacMacro.App.Debugging;

internal enum StoryWireStage
{
    Startup,
    Lobby,
    Units,
    Teams,
    LoadTeam,
    Play,
    StoryMap,
    StoryAct,
    ChallengeType,
    ChallengeState,
    MatchPreview,
    MatchPrestart,
    MatchRuntime,
}

public enum StoryWireStageStatus
{
    Waiting,
    Running,
    Passed,
    Failed,
}

internal enum WireGameMode
{
    Story,
    Raid,
    Challenge,
    Expedition,
    Event,
}

internal static class WireGameModeRepeatPolicy
{
    public static bool Supports(WireGameMode mode) => mode is
        WireGameMode.Story or
        WireGameMode.Raid or
        WireGameMode.Expedition or
        WireGameMode.Event;
}

internal sealed record StoryWireNavigationKeys(
    int? PlayMenu,
    int? UnitInventory,
    int? AreasMenu);

internal sealed record StoryWireTestOptions(
    DebugEvidenceMode Mode,
    WireGameMode GameMode,
    int TeamNumber,
    string Map,
    StoryAct Act,
    StoryDifficulty Difficulty,
    IReadOnlyList<RegularChallengeType> ChallengeTypes,
    StoryWireNavigationKeys NavigationKeys,
    PlacementRuntimeKeys PlacementKeys,
    int ShiftLockVirtualKey,
    string Device,
    bool RunMatchRuntime,
    bool RepeatStage,
    int ExpeditionDifficulty = 1,
    int BossesBeforeExtract = 1,
    bool ExtractAtCheckpoint = true,
    string ExpeditionRewardTarget = "None",
    bool SkipTeamLoad = false);

internal sealed record StoryWireProgress(
    StoryWireStage Stage,
    StoryWireStageStatus Status,
    string Detail,
    IReadOnlyList<string> Events,
    IReadOnlyList<WireVisualComparison>? VisualComparisons = null);

internal sealed record WireVisualComparison(
    string State,
    string Label,
    string OcrBounds,
    string ImageBounds,
    string ImageStatus,
    double Score,
    long OcrMilliseconds,
    long BuildMilliseconds,
    long MatchMilliseconds,
    string Strategy,
    bool Agrees,
    [property: JsonIgnore] GrayImage MedianPreview,
    [property: JsonIgnore] GrayImage ReliabilityPreview,
    [property: JsonIgnore] GrayImage MatchedPreview)
{
    public string Timing => $"OCR {OcrMilliseconds} | IMG {MatchMilliseconds} | BUILD {BuildMilliseconds} MS";

    public string Coordinates => $"OCR {OcrBounds} | IMG {ImageBounds} | {Strategy}";

    public string Agreement => Agrees ? "AGREE" : "DIFF";
}

internal sealed record StoryWireTestResult(
    bool Succeeded,
    StoryWireStage Stage,
    string Status,
    DateTimeOffset? UnavailableUntilUtc = null,
    bool DailyLimitReached = false,
    MatchTerminalOutcome? Outcome = null);

internal sealed record ChallengeNavigationResult(
    bool Succeeded,
    string Status,
    string? Map,
    RegularChallengeType? Type,
    DateTimeOffset? UnavailableUntilUtc,
    bool DailyLimitReached);

internal sealed record WireImageStateResult(
    bool IsMatch,
    string Status,
    IReadOnlyList<string> Events,
    IReadOnlyList<WireVisualComparison> Comparisons,
    int MatchedCount = 0,
    int RequiredMatches = 0);
