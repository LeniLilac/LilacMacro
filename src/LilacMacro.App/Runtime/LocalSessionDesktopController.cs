using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using LilacMacro.App.Views;
using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.App.Runtime;

internal sealed class LocalSessionDesktopController : IAsyncDisposable
{
    private readonly LocalSessionPaths paths = LocalSessionPaths.CreateDefault(AppContext.BaseDirectory);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<SessionWorkerEvent>> pending = [];
    private CancellationTokenSource? heartbeatCancellation;
    private Task? heartbeatTask;
    private Task? readerTask;
    private TaskCompletionSource<SessionWorkerEvent>? runCompletion;
    private SessionWorkerEvent? connectedReady;
    private NamedPipeClientStream? pipe;

    public event EventHandler<SessionWorkerEvent>? EventReceived;

    public async Task<LocalSessionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        LocalSessionStatusStore store = new(paths);
        LocalSessionStatus status = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        LocalSessionStatus reconciled = LocalSessionValidation.ReconcileInterruptedOperation(
            status,
            File.Exists(paths.JournalPath),
            IsSetupHelperRunning());
        if (!ReferenceEquals(status, reconciled))
        {
            await store.WriteAsync(reconciled, cancellationToken).ConfigureAwait(false);
            status = reconciled;
        }
        if (status.State is not (LocalSessionState.Ready or LocalSessionState.Degraded)
            || !File.Exists(paths.JournalPath)) return status;

        LocalSessionCompatibilityResult compatibility = await new LocalSessionCompatibilityProbe(paths)
            .ProbeAsync(LocalSessionProbePurpose.Health, cancellationToken)
            .ConfigureAwait(false);
        if (compatibility.IsCompatible) return status;
        return status with
        {
            State = LocalSessionState.Degraded,
            StatusCode = "native-compatibility-changed",
            Detail = "Windows or the pinned native payload changed. Repair must revalidate the local runner.",
            CompatibilityPassed = false,
            Problems = compatibility.Problems,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<LocalSessionStatus> MutateAsync(string verb, CancellationToken cancellationToken = default)
    {
        if (verb is not ("install" or "repair" or "remove")) throw new ArgumentOutOfRangeException(nameof(verb));
        string helper = Path.Combine(AppContext.BaseDirectory, "LilacMacro.SessionSetup.exe");
        if (!File.Exists(helper)) throw new FileNotFoundException("The signed local-session setup helper is missing.", helper);
        using Process process = Process.Start(new ProcessStartInfo(helper, verb)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        }) ?? throw new InvalidOperationException("Windows did not start the local-session setup helper.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        LocalSessionStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException(status.Problems.FirstOrDefault() ?? status.Detail);
        return status;
    }

    public async Task<SessionWorkerEvent> ConnectAsync(CancellationToken cancellationToken)
    {
        if (pipe is { IsConnected: true } && connectedReady is not null) return connectedReady;
        LocalSessionStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.State is not (LocalSessionState.Ready or LocalSessionState.Degraded))
            throw new InvalidOperationException(status.Problems.FirstOrDefault() ?? status.Detail);
        if (!status.CompatibilityPassed || !status.LoopbackIsolationPassed)
            throw new InvalidOperationException(status.Problems.FirstOrDefault() ?? status.Detail);
        LocalSessionProvisioningManifest manifest = await new ProvisioningJournalStore(paths).ReadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The local runner provisioning journal is missing.");
        await DisconnectAsync().ConfigureAwait(false);
        StartRdpClient();
        pipe = await SessionPipeClient.ConnectValidatedAsync(paths, manifest.RunnerSid, TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
        SessionWorkerCommand handshake = new()
        {
            Kind = SessionWorkerCommandKind.Handshake,
            ControllerVersion = BuildVersion(),
        };
        await SessionPipe.WriteAsync(pipe, handshake, cancellationToken).ConfigureAwait(false);
        SessionWorkerEvent accepted = await SessionPipe.ReadAsync<SessionWorkerEvent>(pipe, cancellationToken).ConfigureAwait(false);
        if (accepted.Kind != SessionWorkerEventKind.HandshakeAccepted || accepted.InReplyTo != handshake.MessageId)
            throw new InvalidDataException("The local runner rejected the controller handshake.");
        SessionWorkerEvent ready = await SessionPipe.ReadAsync<SessionWorkerEvent>(pipe, cancellationToken).ConfigureAwait(false);
        if (ready.Kind != SessionWorkerEventKind.Ready) throw new InvalidOperationException(ready.Detail);
        connectedReady = ready;
        heartbeatCancellation = new CancellationTokenSource();
        readerTask = ReadEventsAsync(heartbeatCancellation.Token);
        heartbeatTask = MaintainHeartbeatAsync(heartbeatCancellation.Token);
        return ready;
    }

    public async Task<long> PublishSnapshotAsync(
        MacroOwnerState ownerState,
        PlanPrototype plan,
        CancellationToken cancellationToken)
    {
        LocalSessionProvisioningManifest manifest = await new ProvisioningJournalStore(paths).ReadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The local runner provisioning journal is missing.");
        long revision = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RunnerRuntimeSnapshot snapshot = await new RunnerSnapshotBuilder().BuildAsync(
            ownerState,
            plan,
            manifest.OwnerSid,
            BuildVersion(),
            revision,
            ReadPlacementSetups(),
            cancellationToken).ConfigureAwait(false);
        await new RunnerSnapshotStore(paths).PublishAsync(snapshot, manifest.OwnerSid, cancellationToken).ConfigureAwait(false);
        return revision;
    }

    public async Task RunAsync(
        long revision,
        string privateServerLink,
        IProgress<SessionRuntimeProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        await RequestAsync(SessionWorkerCommandKind.SelectSnapshot, revision, default, cancellationToken).ConfigureAwait(false);
        TaskCompletionSource<SessionWorkerEvent> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runCompletion = completion;
        try
        {
            EventHandler<SessionWorkerEvent> handler = (_, value) => ReportProgress(value, progress);
            EventReceived += handler;
            try
            {
                JsonElement payload = JsonSerializer.SerializeToElement(
                    new SessionStartRequest { PrivateServerLink = privateServerLink }, AtomicJsonFile.Options);
                SessionWorkerEvent started = await RequestAsync(
                    SessionWorkerCommandKind.Start, revision, payload, cancellationToken).ConfigureAwait(false);
                if (started.Kind != SessionWorkerEventKind.Running)
                    throw new InvalidOperationException(started.Detail);
                await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { EventReceived -= handler; }
        }
        catch (OperationCanceledException) when (pipe is { IsConnected: true })
        {
            using CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
            try { await RequestAsync(SessionWorkerCommandKind.Stop, revision, default, stop.Token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or InvalidOperationException) { }
            throw;
        }
        finally { runCompletion = null; }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        writeGate.Dispose();
    }

    private static JsonElement ReadPlacementSetups()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LilacMacro", "placements");
        Dictionary<string, JsonElement> documents = new(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
                documents[Path.GetFileName(file)] = document.RootElement.Clone();
            }
        }
        return JsonSerializer.SerializeToElement(documents, AtomicJsonFile.Options);
    }

    private static void StartRdpClient()
    {
        string arguments = $"/v:127.0.0.1:{TermServiceConfigurationManager.LocalPort} /f";
        _ = Process.Start(new ProcessStartInfo("mstsc.exe", arguments) { UseShellExecute = true });
    }

    private static bool IsSetupHelperRunning()
    {
        Process[] processes = Process.GetProcessesByName("LilacMacro.SessionSetup");
        try { return processes.Length > 0; }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    private async Task MaintainHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            await RequestAsync(SessionWorkerCommandKind.Ping, 0, default, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SessionWorkerEvent> RequestAsync(
        SessionWorkerCommandKind kind,
        long revision,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        PipeStream connected = pipe is { IsConnected: true } value
            ? value
            : throw new InvalidOperationException("The local runner is not connected.");
        SessionWorkerCommand command = new()
        {
            Kind = kind,
            SnapshotRevision = revision,
            ControllerVersion = BuildVersion(),
            Payload = payload,
        };
        TaskCompletionSource<SessionWorkerEvent> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(command.MessageId, response)) throw new InvalidOperationException("Duplicate runner command identifier.");
        try
        {
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await SessionPipe.WriteAsync(connected, command, cancellationToken).ConfigureAwait(false); }
            finally { writeGate.Release(); }
            SessionWorkerEvent result = await response.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result.Kind == SessionWorkerEventKind.Error) throw new InvalidOperationException(result.Detail);
            return result;
        }
        finally { pending.TryRemove(command.MessageId, out _); }
    }

    private async Task ReadEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            PipeStream connected = pipe ?? throw new InvalidOperationException("The local runner is not connected.");
            while (!cancellationToken.IsCancellationRequested && connected.IsConnected)
            {
                SessionWorkerEvent value = await SessionPipe.ReadAsync<SessionWorkerEvent>(connected, cancellationToken).ConfigureAwait(false);
                if (value.InReplyTo is Guid reply && pending.TryGetValue(reply, out TaskCompletionSource<SessionWorkerEvent>? waiter))
                    waiter.TrySetResult(value);
                else
                {
                    EventReceived?.Invoke(this, value);
                    if (value.Code == "run-completed") runCompletion?.TrySetResult(value);
                    else if (value.Code == "run-canceled") runCompletion?.TrySetCanceled();
                    else if (value.Kind == SessionWorkerEventKind.Error) runCompletion?.TrySetException(new InvalidOperationException(value.Detail));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            foreach (TaskCompletionSource<SessionWorkerEvent> waiter in pending.Values) waiter.TrySetException(exception);
            runCompletion?.TrySetException(exception);
        }
    }

    private async Task DisconnectAsync()
    {
        heartbeatCancellation?.Cancel();
        foreach (Task task in new[] { heartbeatTask, readerTask }.Where(task => task is not null).Cast<Task>())
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
        heartbeatCancellation?.Dispose();
        heartbeatCancellation = null;
        heartbeatTask = null;
        readerTask = null;
        pipe?.Dispose();
        pipe = null;
        connectedReady = null;
    }

    private static void ReportProgress(SessionWorkerEvent value, IProgress<SessionRuntimeProgress> progress)
    {
        if (value.Payload.ValueKind is not JsonValueKind.Object) return;
        SessionRuntimeProgress? update = value.Payload.Deserialize<SessionRuntimeProgress>(AtomicJsonFile.Options);
        if (update is not null) progress.Report(update);
    }

    private static string BuildVersion() => typeof(LocalSessionDesktopController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
