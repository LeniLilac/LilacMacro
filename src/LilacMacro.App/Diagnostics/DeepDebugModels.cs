using System.Collections.Concurrent;
using System.Threading.Channels;

namespace LilacMacro.App.Diagnostics;

public sealed record DeepDebugOptions
{
    public const int DefaultFrameRetentionMinutes = 30;

    public const int DefaultCaptureIntervalMilliseconds = 5_000;

    public const int DefaultRetainedArchiveCount = 10;

    public const int MinimumCaptureIntervalMilliseconds = 500;

    public const int MaximumCaptureIntervalMilliseconds = 5_000;

    public bool Enabled { get; init; } = true;

    public int FrameRetentionMinutes { get; init; } = DefaultFrameRetentionMinutes;

    public int RetainedArchiveCount { get; init; } = DefaultRetainedArchiveCount;

    public int CaptureIntervalMilliseconds { get; init; } =
        DefaultCaptureIntervalMilliseconds;

    public static int NormalizeFrameRetention(int value) => Math.Clamp(value, 1, 120);

    public static int NormalizeRetainedArchiveCount(int value) => Math.Clamp(value, 1, 100);

    public static int NormalizeCaptureInterval(int value) => value switch
    {
        500 or 1_000 or 2_000 or 5_000 => value,
        _ => DefaultCaptureIntervalMilliseconds,
    };

    public static int CaptureIntervalIndex(int milliseconds) =>
        NormalizeCaptureInterval(milliseconds) switch
        {
            500 => 0,
            1_000 => 1,
            2_000 => 2,
            _ => 3,
        };
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

    public DeepDebugFrameCaptureLoop? FrameCaptureLoop { get; set; }

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
