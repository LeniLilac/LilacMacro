using System.Text.Json;

namespace LilacMacro.Core.LocalSession;

public enum SessionWorkerCommandKind
{
    Handshake,
    Start,
    Stop,
    SelectSnapshot,
    Ping,
}

public enum SessionWorkerEventKind
{
    HandshakeAccepted,
    Ready,
    Running,
    StateChanged,
    Statistics,
    Error,
    Heartbeat,
    CancellationAcknowledged,
    CaptureUnsupported,
}

public sealed record SessionWorkerCommand
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public SessionWorkerCommandKind Kind { get; init; }
    public long SnapshotRevision { get; init; }
    public string ControllerVersion { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SessionWorkerEvent
{
    public const int CurrentProtocolVersion = SessionWorkerCommand.CurrentProtocolVersion;

    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public Guid? InReplyTo { get; init; }
    public SessionWorkerEventKind Kind { get; init; }
    public string WorkerVersion { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SessionStartRequest
{
    public string PrivateServerLink { get; init; } = string.Empty;
}

public sealed record SessionRuntimeProgress
{
    public string TaskId { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public int Wins { get; init; }
    public int Losses { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public interface ISessionWorkerRuntime
{
    bool IsAvailable(out string detail);

    Task RunAsync(
        RunnerRuntimeSnapshot snapshot,
        SessionStartRequest request,
        IProgress<SessionRuntimeProgress> progress,
        CancellationToken cancellationToken);
}
