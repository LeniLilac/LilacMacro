namespace LilacMacro.App.Lifecycle;

internal enum WindowShutdownDecision
{
    AllowClose,
    CancelAndFlush,
    CancelWhileFlushing,
}

internal sealed class WindowShutdownState
{
    private bool _flushInProgress;
    private bool _readyToClose;

    public WindowShutdownDecision BeginClose()
    {
        if (_readyToClose) return WindowShutdownDecision.AllowClose;
        if (_flushInProgress) return WindowShutdownDecision.CancelWhileFlushing;
        _flushInProgress = true;
        return WindowShutdownDecision.CancelAndFlush;
    }

    public void CompleteFlush()
    {
        EnsureFlushInProgress();
        _flushInProgress = false;
        _readyToClose = true;
    }

    public void FailFlush()
    {
        EnsureFlushInProgress();
        _flushInProgress = false;
    }

    private void EnsureFlushInProgress()
    {
        if (!_flushInProgress)
        {
            throw new InvalidOperationException("No shutdown flush is in progress.");
        }
    }
}
