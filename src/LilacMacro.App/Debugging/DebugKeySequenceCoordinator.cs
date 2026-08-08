using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Debugging;

internal sealed class DebugKeySequenceCoordinator(WorkspaceController workspace)
{
    private AutomationKeySequence? _sequence;
    private CancellationTokenSource? _operationCancellation;
    private Task? _activeTask;

    public event EventHandler? Changed;

    public DebugKeySequenceState State { get; private set; }

    public string Status { get; private set; } = "IDLE";

    public bool OwnsF6 => State != DebugKeySequenceState.Idle;

    public async Task ArmAsync(AutomationKeySequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (State != DebugKeySequenceState.Idle)
        {
            throw new InvalidOperationException("The key chain is already active.");
        }

        _sequence = sequence;
        CancellationTokenSource cancellation = new();
        _operationCancellation = cancellation;
        SetState(DebugKeySequenceState.Arming, "FOCUSING ROBLOX");
        Task focusTask = workspace.FocusRobloxAsync(DebugWorkflowCatalog.ClientSize, cancellation.Token);
        _activeTask = focusTask;
        try
        {
            await focusTask;
            if (State == DebugKeySequenceState.Arming)
            {
                SetState(DebugKeySequenceState.Armed, "ARMED · F6");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _sequence = null;
            SetState(DebugKeySequenceState.Idle, "CANCELLED");
        }
        catch (Exception error)
        {
            _sequence = null;
            SetState(DebugKeySequenceState.Idle, $"ERROR · {error.Message}");
            throw;
        }
        finally
        {
            if (ReferenceEquals(_activeTask, focusTask)) _activeTask = null;
            if (ReferenceEquals(_operationCancellation, cancellation)) _operationCancellation = null;
            cancellation.Dispose();
        }
    }

    public void Disarm()
    {
        if (State != DebugKeySequenceState.Armed) return;
        _sequence = null;
        SetState(DebugKeySequenceState.Idle, "IDLE");
    }

    public bool HandleF6()
    {
        switch (State)
        {
            case DebugKeySequenceState.Armed:
                BeginRun();
                return true;
            case DebugKeySequenceState.Running:
                RequestStop();
                return true;
            case DebugKeySequenceState.Arming:
            case DebugKeySequenceState.Stopping:
                return true;
            default:
                return false;
        }
    }

    public void RequestStop()
    {
        if (State == DebugKeySequenceState.Armed)
        {
            Disarm();
            return;
        }
        if (State is not (DebugKeySequenceState.Arming or DebugKeySequenceState.Running)) return;
        SetState(DebugKeySequenceState.Stopping, "STOPPING");
        _operationCancellation?.Cancel();
    }

    public async Task StopAsync()
    {
        if (State == DebugKeySequenceState.Armed)
        {
            Disarm();
            return;
        }
        if (State == DebugKeySequenceState.Idle) return;
        RequestStop();
        Task active = _activeTask ?? Task.CompletedTask;
        try
        {
            await active;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    private void BeginRun()
    {
        AutomationKeySequence sequence = _sequence
            ?? throw new InvalidOperationException("No key chain is armed.");
        CancellationTokenSource cancellation = new();
        _operationCancellation = cancellation;
        SetState(DebugKeySequenceState.Running, "RUNNING · F6 STOP");
        _activeTask = ExecuteAsync(sequence, cancellation);
    }

    private async Task ExecuteAsync(
        AutomationKeySequence sequence,
        CancellationTokenSource cancellation)
    {
        try
        {
            await workspace.RunKeySequenceAsync(
                DebugWorkflowCatalog.ClientSize,
                sequence,
                cancellation.Token);
            SetState(DebugKeySequenceState.Idle, "COMPLETE");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetState(DebugKeySequenceState.Idle, "CANCELLED");
        }
        catch (Exception error)
        {
            SetState(DebugKeySequenceState.Idle, $"ERROR · {error.Message}");
        }
        finally
        {
            _sequence = null;
            if (ReferenceEquals(_operationCancellation, cancellation)) _operationCancellation = null;
            _activeTask = null;
            cancellation.Dispose();
        }
    }

    private void SetState(DebugKeySequenceState state, string status)
    {
        State = state;
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
