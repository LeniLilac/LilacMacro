using System.Text.Json;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.LocalSession;

public enum ExecutionTarget
{
    LocalDesktop,
    LocalRunnerSession,
}

public enum RunnerConfigurationMode
{
    Shared,
    Isolated,
}

public sealed record LocalRunnerProfile
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string RunnerSid { get; init; } = string.Empty;
    public int Slot { get; init; }
    public string LoopbackAddress { get; init; } = string.Empty;
    public RunnerConfigurationMode ConfigurationMode { get; init; } = RunnerConfigurationMode.Shared;
}

public enum LocalSessionState
{
    Absent,
    Installing,
    Ready,
    Degraded,
    Removing,
    RecoveryRequired,
}

public sealed record LocalSessionStatus
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public LocalSessionState State { get; init; } = LocalSessionState.Absent;
    public string StatusCode { get; init; } = "absent";
    public string Detail { get; init; } = "Local instance manager is not installed.";
    public bool CompatibilityPassed { get; init; }
    public bool LoopbackIsolationPassed { get; init; }
    public bool FreshCapturePassed { get; init; }
    public bool RuntimeHostPassed { get; init; }
    public string PolicyVersion { get; init; } = string.Empty;
    public string WorkerVersion { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<string> Problems { get; init; } = [];

    public bool CanRun => State == LocalSessionState.Ready
        && CompatibilityPassed
        && LoopbackIsolationPassed
        && FreshCapturePassed
        && RuntimeHostPassed
        && Problems.Count == 0;

    public bool CanOpenInteractiveSession =>
        State is LocalSessionState.Ready or LocalSessionState.Degraded
        && CompatibilityPassed
        && LoopbackIsolationPassed;
}

public sealed record NativePayloadFile(string RelativePath, long Size, string Sha256);

public sealed record LocalSessionCompatibilityEvidence
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ProbeVersion { get; init; } = "termwrap-self-scan-v3";
    public string OsBuild { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string TermServiceSha256 { get; init; } = string.Empty;
    public string TermWrapSha256 { get; init; } = string.Empty;
    public bool RequiredPatchesPassed { get; init; }
    public IReadOnlyList<string> RequiredPatchDiagnostics { get; init; } = [];
    public IReadOnlyList<string> AdvisoryDiagnostics { get; init; } = [];
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LocalSessionCompatibilityResult
{
    public bool IsCompatible { get; init; }
    public bool UsedCachedEvidence { get; init; }
    public string OsBuild { get; init; } = string.Empty;
    public string TermServiceSha256 { get; init; } = string.Empty;
    public string TermWrapSha256 { get; init; } = string.Empty;
    public LocalSessionCompatibilityEvidence? Evidence { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = [];
}

public sealed record OwnedSessionResource(string Kind, string Identifier);

public sealed record OriginalSystemValue(
    string Kind,
    string Identifier,
    bool Existed,
    string? ValueType,
    string? EncodedValue);

public sealed record LocalSessionProvisioningManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid OperationId { get; init; } = Guid.NewGuid();
    public LocalSessionState State { get; init; } = LocalSessionState.Absent;
    public string OwnerSid { get; init; } = string.Empty;
    public string RunnerSid { get; init; } = string.Empty;
    public string RunnerAccountName { get; init; } = "LilacMacroRunner";
    public IReadOnlyList<LocalRunnerProfile> RunnerProfiles { get; init; } = [];
    public string OsBuild { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public string WorkerVersion { get; init; } = string.Empty;
    public string PolicyVersion { get; init; } = string.Empty;
    public string NativePayloadVersion { get; init; } = string.Empty;
    public LocalSessionCompatibilityEvidence? CompatibilityEvidence { get; init; }
    public IReadOnlyList<NativePayloadFile> NativePayload { get; init; } = [];
    public IReadOnlyList<OwnedSessionResource> OwnedResources { get; init; } = [];
    public IReadOnlyList<OriginalSystemValue> OriginalSystemState { get; init; } = [];
    public IReadOnlyList<string> CompletedSteps { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RunnerRuntimeSnapshot
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public string AppVersion { get; init; } = string.Empty;
    public string OwnerSid { get; init; } = string.Empty;
    public ExecutionTarget ExecutionTarget { get; init; } = ExecutionTarget.LocalRunnerSession;
    public string PlanName { get; init; } = string.Empty;
    public IReadOnlyList<RunnerTaskSnapshot> Tasks { get; init; } = [];
    public JsonElement PlacementSetups { get; init; }
    public IReadOnlyList<RunnerStateContextSnapshot> StateContexts { get; init; } = [];
    public IReadOnlyDictionary<string, int?> KeyBindings { get; init; } =
        new Dictionary<string, int?>(StringComparer.Ordinal);
    public string OcrModel { get; init; } = "PP-OCRv6_small_rec";
    public bool PreferGpu { get; init; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public enum RunnerTaskMode
{
    Story,
    Raid,
    Challenge,
    Expedition,
    Event,
    Utilities,
}

public sealed record RunnerTaskSnapshot
{
    public string Id { get; init; } = string.Empty;
    public int Priority { get; init; }
    public RunnerTaskMode Mode { get; init; }
    public string Route { get; init; } = string.Empty;
    public int Target { get; init; } = 1;
    public int DefeatRetries { get; init; }
    public bool HardMode { get; init; }
    public bool RunTrait { get; init; } = true;
    public bool RunStat { get; init; } = true;
    public bool RunSprite { get; init; } = true;
    public int Difficulty { get; init; } = 1;
    public int InfiniteWave { get; init; } = 140;
    public int BossesBeforeExtract { get; init; } = 1;
    public bool ExtractAtCheckpoint { get; init; } = true;
    public string RewardTarget { get; init; } = "None";
    public IReadOnlyList<string> ShopItemIds { get; init; } = [];
}

public sealed record RunnerVisualAnchorSnapshot(
    string Text,
    OcrMatchMode MatchMode,
    OcrSpatialSelector SpatialSelector,
    string? SpatialAnchorText);

public sealed record RunnerStateContextSnapshot(
    string State,
    PixelRect RegionOfInterest,
    IReadOnlyList<RunnerVisualAnchorSnapshot> VisualAnchors);
