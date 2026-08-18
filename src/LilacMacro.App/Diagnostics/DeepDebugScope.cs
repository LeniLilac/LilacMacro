namespace LilacMacro.App.Diagnostics;

public sealed class DeepDebugScope
{
    private readonly DeepDebugSessionService _service;
    private readonly DeepDebugSession _session;
    private int _completed;

    internal DeepDebugScope(DeepDebugSessionService service, DeepDebugSession session)
    {
        _service = service;
        _session = session;
    }

    public async Task CompleteAsync(string outcome, Exception? error = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        try
        {
            await _service.CompleteAsync(_session, outcome, error);
        }
        catch (Exception finalizationError)
        {
            _service.PreserveFinalizationError(_session, finalizationError);
        }
    }
}
