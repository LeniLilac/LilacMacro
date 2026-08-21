using System.Collections.Concurrent;
using System.Threading.Channels;
using LilacMacro.Core.Services;

namespace LilacMacro.App.Diagnostics;

public sealed record DeepDebugOptions
{
    public const int CaptureIntervalMilliseconds = 1_000;

    public const int DefaultMaximumArchiveStorageGiB = 10;

    public bool Enabled { get; init; } = true;

    public int MaximumArchiveStorageGiB { get; init; } =
        DefaultMaximumArchiveStorageGiB;

    public static int NormalizeMaximumArchiveStorageGiB(int value) =>
        Math.Clamp(value, 1, 1_024);
}

internal sealed record DeepDebugArchiveLimits(
    long MaximumArchiveBytes,
    long FrameEvidenceBytes,
    long EventBytes,
    long TimelineBytes,
    long ConfigurationBytes,
    long CrashLogBytes)
{
    public static DeepDebugArchiveLimits Production { get; } = new(
        DiagnosticUploadPolicy.MaximumArchiveBytes,
        5 * DiagnosticUploadPolicy.OneGiB / 2,
        128L * 1024 * 1024,
        64L * 1024 * 1024,
        8L * 1024 * 1024,
        1L * 1024 * 1024);
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

    public required DeepDebugArchiveLimits Limits { get; init; }

    public required DeepDebugEvidenceRetention Evidence { get; init; }

    public DeepDebugFrameCaptureLoop? FrameCaptureLoop { get; set; }

    public ConcurrentDictionary<string, DeepDebugVisualProfileReference> VisualProfiles { get; } =
        new(StringComparer.Ordinal);

    public Task WriterTask { get; set; } = Task.CompletedTask;

    public Exception? WriterFailure { get; set; }

    public long Sequence;
    public int ArtifactCount;
    public int EventCount;
    public int InputEventCount;
    public int DiscardedArtifactCount;
    public int WrittenEventCount;
    public int DiscardedEventCount;
    public bool TimelineTruncated;
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
    int RetainedArtifacts,
    int DiscardedArtifacts,
    int RetainedEvents,
    int DiscardedEvents,
    bool TimelineTruncated,
    int ErrorWindows,
    int ErrorWindowsDiscarded,
    int TransitionFrames,
    long FrameEvidenceBytes,
    long MaximumArchiveBytes,
    int VisualProfiles,
    string ArtifactPolicy,
    string? WriterFailure,
    string? OperationError,
    string PrivacyPolicy);
