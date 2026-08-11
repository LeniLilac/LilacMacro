using System.Collections.Concurrent;
using System.Threading.Channels;

namespace LilacMacro.App.Diagnostics;

public sealed record DeepDebugOptions
{
    public bool Enabled { get; init; }

    public int FrameRetentionMinutes { get; init; } = 15;

    public static int NormalizeFrameRetention(int value) => Math.Clamp(value, 1, 120);
}

public sealed record DeepDebugOperationContext(
    string Surface,
    object? Settings = null,
    string? Dataset = null);

internal sealed class DeepDebugSession
{
    public required string Operation { get; init; }

    public required DeepDebugOperationContext Context { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required string StagingDirectory { get; init; }

    public required Channel<DeepDebugWriteItem> Channel { get; init; }

    public required int FrameRetentionMinutes { get; init; }

    public bool RetainsAllFrames => FrameRetentionMinutes == 0;

    public PriorityQueue<DeepDebugRetainedFrame, long> RetainedFrames { get; } = new();

    public ConcurrentDictionary<string, DeepDebugVisualProfileReference> VisualProfiles { get; } =
        new(StringComparer.Ordinal);

    public Task WriterTask { get; set; } = Task.CompletedTask;

    public Exception? WriterFailure { get; set; }

    public long Sequence;
    public int ArtifactCount;
    public int EventCount;
    public int InputEventCount;
    public int DiscardedArtifactCount;
}

internal sealed record DeepDebugWriteItem(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Category,
    string Action,
    object? Data,
    string? ArtifactPath,
    byte[]? ArtifactBytes);

internal sealed record DeepDebugEventRecord(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Category,
    string Action,
    string? Artifact,
    object? Data);

internal sealed record DeepDebugRetainedFrame(string Path, DateTimeOffset TimestampUtc);

internal sealed record DeepDebugVisualProfileReference(
    string ProfileId,
    string RevisionDirectory,
    string? LocatorPath);

internal sealed record DeepDebugManifest(
    int FormatVersion,
    string Operation,
    string Outcome,
    string AppVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Runtime,
    int Artifacts,
    int Events,
    int InputEvents,
    int FrameRetentionMinutes,
    int RetainedArtifacts,
    int DiscardedArtifacts,
    int VisualProfiles,
    string ArtifactPolicy,
    string? WriterFailure,
    string? OperationError,
    string PrivacyPolicy);
