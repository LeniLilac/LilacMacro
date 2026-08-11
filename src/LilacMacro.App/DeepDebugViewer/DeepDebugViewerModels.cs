using System.Text.Json;

namespace LilacMacro.App.DeepDebugViewer;

internal sealed record DeepDebugManifestSummary(
    string Operation,
    string Outcome,
    string AppVersion,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    TimeSpan? Runtime,
    int DeclaredArtifacts,
    int DeclaredEvents,
    int DeclaredInputEvents,
    int VisualProfiles);

internal sealed record DeepDebugTimelineEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Category,
    string Action,
    string? ArtifactPath,
    string Details,
    JsonElement Data);

internal readonly record struct DeepDebugSourceRegion(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) =>
        x >= X && y >= Y && x < X + Width && y < Y + Height;
}

internal sealed record DeepDebugFrameRecord(
    int Index,
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Path,
    int EventIndex,
    bool EntryExists,
    DeepDebugSourceRegion? SourceRegion);

internal sealed record DeepDebugInputMarker(
    int Number,
    string Kind,
    int ClientX,
    int ClientY,
    int LocalX,
    int LocalY,
    int? WheelDelta,
    DateTimeOffset TimestampUtc);

internal sealed record DeepDebugArchiveIndex(
    DeepDebugManifestSummary Manifest,
    IReadOnlyList<DeepDebugTimelineEvent> Events,
    IReadOnlyList<DeepDebugFrameRecord> Frames,
    int MalformedEventLines);
