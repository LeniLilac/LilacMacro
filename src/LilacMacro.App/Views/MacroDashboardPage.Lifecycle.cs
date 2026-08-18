namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    public bool SetDashboardActive(bool active, out string error)
    {
        if (!active && _runTask is not null)
        {
            error = "Stop the macro before leaving the Macro tab.";
            return false;
        }
        return RobloxDock.SetDashboardActive(active, out error);
    }

    public bool TryPrepareForClose(out string error)
    {
        _runCancellation?.Cancel();
        return RobloxDock.TryPrepareForClose(out error);
    }

    public async Task CompleteForCloseAsync()
    {
        _lifecycleCancellation.Cancel();
        if (_ocrSetupTask is not null)
        {
            try { await _ocrSetupTask; }
            catch (OperationCanceledException) { }
        }
        _runCancellation?.Cancel();
        if (_runTask is not null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }
        await FlushRuntimeProgressAsync();
        await CompleteDebugAsync("closed");
        await _discordEvents.DisposeAsync();
        _deepDebugFrameCaptureRegistration.Dispose();
        _ocr.Dispose();
        _workspace.Dispose();
        _ownerState.RuntimeProgressResetRequested -= OwnerState_OnRuntimeProgressResetRequested;
        _lifecycleCancellation.Dispose();
    }
}
