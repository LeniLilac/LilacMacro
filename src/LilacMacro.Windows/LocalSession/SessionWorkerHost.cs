using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.Capture;

namespace LilacMacro.Windows.LocalSession;

public sealed class SessionWorkerHost(
    LocalSessionPaths paths,
    string workerVersion,
    ISessionWorkerRuntime runtime)
{
    private readonly LocalSessionStatusStore statusStore = new(paths);
    private readonly SemaphoreSlim emitGate = new(1, 1);
    private readonly object runtimeSync = new();
    private CancellationTokenSource? activeRun;
    private Task? activeRunTask;
    private RunnerRuntimeSnapshot? selectedSnapshot;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        LocalSessionProvisioningManifest manifest = await new ProvisioningJournalStore(paths).ReadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The runner provisioning journal is missing.");
        string currentSid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
        if (!string.Equals(currentSid, manifest.RunnerSid, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The session worker is not running as the provisioned runner SID.");
        RunnerProcessAccessManager.GrantOwnerValidationAccess(manifest.OwnerSid);

        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = SessionPipe.CreateServer(manifest.OwnerSid, manifest.RunnerSid);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            try { await ServeConnectionAsync(pipe, manifest, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException
                or OperationCanceledException
                or InvalidDataException
                or UnauthorizedAccessException)
            {
            }
            finally
            {
                selectedSnapshot = null;
                await StopRuntimeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ServeConnectionAsync(NamedPipeServerStream pipe, LocalSessionProvisioningManifest manifest, CancellationToken cancellationToken)
    {
        SessionWorkerCommand handshake = await SessionPipe.ReadAsync<SessionWorkerCommand>(pipe, cancellationToken).ConfigureAwait(false);
        if (handshake.ProtocolVersion != SessionWorkerCommand.CurrentProtocolVersion || handshake.Kind != SessionWorkerCommandKind.Handshake)
            throw new InvalidDataException("Worker handshake is incompatible.");
        if (!string.Equals(handshake.ControllerVersion, workerVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Worker and controller versions do not match.");
        await EmitAsync(pipe, SessionWorkerEventKind.HandshakeAccepted, handshake.MessageId, "handshake-ok", "Worker identity and protocol validated.", cancellationToken).ConfigureAwait(false);

        bool fresh = VerifyFreshCapture();
        bool runtimeReady = runtime.IsAvailable(out string runtimeDetail);
        bool ready = fresh && runtimeReady;
        await statusStore.WriteAsync(new LocalSessionStatus
        {
            State = ready ? LocalSessionState.Ready : LocalSessionState.Degraded,
            StatusCode = ready ? "ready" : fresh ? "runtime-host-unavailable" : "capture-unsupported",
            Detail = ready
                ? "Fresh capture and the shared headless workflow runtime are ready."
                : fresh ? runtimeDetail : "WGC did not produce a usable Roblox frame in this session state.",
            CompatibilityPassed = true,
            LoopbackIsolationPassed = true,
            FreshCapturePassed = fresh,
            RuntimeHostPassed = runtimeReady,
            PolicyVersion = manifest.PolicyVersion,
            WorkerVersion = workerVersion,
            Problems = ready ? [] : fresh
                ? [runtimeDetail]
                : ["Keep the local RDP client visibly connected; hidden, minimized, or disconnected capture is unsupported."],
        }, cancellationToken).ConfigureAwait(false);
        await EmitAsync(
            pipe,
            ready ? SessionWorkerEventKind.Ready : fresh ? SessionWorkerEventKind.Error : SessionWorkerEventKind.CaptureUnsupported,
            null,
            ready ? "ready" : fresh ? "runtime-host-unavailable" : "capture-unsupported",
            ready ? "Runner is ready." : fresh ? runtimeDetail : "Fresh WGC frame unavailable.",
            cancellationToken).ConfigureAwait(false);

        if (!ready) return;

        while (pipe.IsConnected)
        {
            using CancellationTokenSource heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
            SessionWorkerCommand command = await SessionPipe.ReadAsync<SessionWorkerCommand>(pipe, heartbeat.Token).ConfigureAwait(false);
            if (command.ProtocolVersion != SessionWorkerCommand.CurrentProtocolVersion) throw new InvalidDataException("Command protocol mismatch.");
            try { await HandleAsync(pipe, command, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
            {
                await EmitAsync(pipe, SessionWorkerEventKind.Error, command.MessageId, "command-rejected", exception.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleAsync(PipeStream pipe, SessionWorkerCommand command, CancellationToken cancellationToken)
    {
        switch (command.Kind)
        {
            case SessionWorkerCommandKind.Ping:
                await EmitAsync(pipe, SessionWorkerEventKind.Heartbeat, command.MessageId, "heartbeat", "Worker responsive.", cancellationToken).ConfigureAwait(false);
                break;
            case SessionWorkerCommandKind.SelectSnapshot:
                RunnerRuntimeSnapshot? snapshot = await new RunnerSnapshotStore(paths).ReadAsync(cancellationToken).ConfigureAwait(false);
                if (snapshot is null || snapshot.Revision != command.SnapshotRevision) throw new InvalidDataException("Requested snapshot revision is unavailable.");
                LocalSessionProvisioningManifest manifest = await new ProvisioningJournalStore(paths).ReadAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The runner provisioning journal is missing.");
                LocalSessionValidationResult validation = LocalSessionValidation.Validate(snapshot, manifest.OwnerSid, workerVersion);
                if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
                selectedSnapshot = snapshot;
                await EmitAsync(pipe, SessionWorkerEventKind.StateChanged, command.MessageId, "snapshot-selected", $"Snapshot revision {snapshot.Revision} validated.", cancellationToken).ConfigureAwait(false);
                break;
            case SessionWorkerCommandKind.Start:
                if (selectedSnapshot is null || selectedSnapshot.Revision != command.SnapshotRevision)
                    throw new InvalidDataException("Select the requested snapshot before starting the runner.");
                SessionStartRequest request = command.Payload.Deserialize<SessionStartRequest>(AtomicJsonFile.Options)
                    ?? throw new InvalidDataException("Runner start payload is missing.");
                StartRuntime(pipe, selectedSnapshot, request, cancellationToken);
                await EmitAsync(pipe, SessionWorkerEventKind.Running, command.MessageId, "running", "Runner workflow started.", cancellationToken).ConfigureAwait(false);
                break;
            case SessionWorkerCommandKind.Stop:
                await StopRuntimeAsync().ConfigureAwait(false);
                await EmitAsync(pipe, SessionWorkerEventKind.CancellationAcknowledged, command.MessageId, "stopped", "Runner released workflow ownership.", cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException("Unsupported worker command.");
        }
    }

    private static bool VerifyFreshCapture()
    {
        try
        {
            RobloxWindowService windows = new();
            RobloxWindow? window = windows.FindBest();
            if (window is null) return false;
            using RobloxCaptureService capture = new(windows);
            byte[] png = capture.Capture(window.Value).Bytes;
            return png.Length > 256;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException) { return false; }
    }

    private void StartRuntime(
        PipeStream pipe,
        RunnerRuntimeSnapshot snapshot,
        SessionStartRequest request,
        CancellationToken connectionToken)
    {
        lock (runtimeSync)
        {
            if (activeRunTask is { IsCompleted: false }) throw new InvalidOperationException("The runner already owns an active workflow.");
            activeRun?.Dispose();
            activeRun = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
            CancellationToken runToken = activeRun.Token;
            IProgress<SessionRuntimeProgress> progress = new Progress<SessionRuntimeProgress>(value =>
                _ = EmitProgressSafeAsync(pipe, value, runToken));
            activeRunTask = RunRuntimeAsync(pipe, snapshot, request, progress, runToken);
        }
    }

    private async Task RunRuntimeAsync(
        PipeStream pipe,
        RunnerRuntimeSnapshot snapshot,
        SessionStartRequest request,
        IProgress<SessionRuntimeProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtime.RunAsync(snapshot, request, progress, cancellationToken).ConfigureAwait(false);
            await EmitSafeAsync(pipe, SessionWorkerEventKind.StateChanged, "run-completed", "Runner plan completed.").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await EmitSafeAsync(pipe, SessionWorkerEventKind.StateChanged, "run-canceled", "Runner plan canceled.").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await EmitSafeAsync(pipe, SessionWorkerEventKind.Error, "run-failed", exception.Message).ConfigureAwait(false);
        }
    }

    private async Task StopRuntimeAsync()
    {
        Task? running;
        lock (runtimeSync)
        {
            activeRun?.Cancel();
            running = activeRunTask;
        }
        if (running is not null)
        {
            try { await running.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("The runner did not acknowledge cancellation and still owns workflow input.");
            }
        }
        lock (runtimeSync)
        {
            activeRun?.Dispose();
            activeRun = null;
            activeRunTask = null;
        }
    }

    private Task MarkCaptureUnsupportedAsync(CancellationToken cancellationToken) =>
        statusStore.WriteAsync(new LocalSessionStatus
        {
            State = LocalSessionState.Degraded,
            StatusCode = "capture-unsupported",
            Detail = "Automation stopped because the runner frame became stale or unavailable.",
            CompatibilityPassed = true,
            LoopbackIsolationPassed = true,
            FreshCapturePassed = false,
            RuntimeHostPassed = false,
            WorkerVersion = workerVersion,
            Problems = ["Keep the local RDP client visibly connected; hidden, minimized, or disconnected capture is unsupported."],
        }, cancellationToken);

    private async Task EmitProgressSafeAsync(PipeStream pipe, SessionRuntimeProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            await EmitAsync(
                pipe,
                SessionWorkerEventKind.StateChanged,
                null,
                "runtime-progress",
                progress.Detail,
                cancellationToken,
                JsonSerializer.SerializeToElement(progress, AtomicJsonFile.Options)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task EmitSafeAsync(PipeStream pipe, SessionWorkerEventKind kind, string code, string detail)
    {
        try { await EmitAsync(pipe, kind, null, code, detail, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }
    }

    private async Task EmitAsync(
        PipeStream pipe,
        SessionWorkerEventKind kind,
        Guid? reply,
        string code,
        string detail,
        CancellationToken cancellationToken,
        JsonElement payload = default)
    {
        await emitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SessionPipe.WriteAsync(pipe, new SessionWorkerEvent
            {
                Kind = kind,
                InReplyTo = reply,
                WorkerVersion = workerVersion,
                Code = code,
                Detail = detail,
                Payload = payload,
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { emitGate.Release(); }
    }
}
