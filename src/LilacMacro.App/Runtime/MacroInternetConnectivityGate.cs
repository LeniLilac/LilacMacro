namespace LilacMacro.App.Runtime;

internal sealed class MacroInternetConnectivityGate
{
    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    private readonly Func<bool> _isAvailable;
    private readonly Action<string> _appendLog;
    private readonly TimeSpan _pollInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MacroInternetConnectivityGate(
        Func<bool> isAvailable,
        Action<string> appendLog,
        TimeSpan? pollInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _isAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
        _appendLog = appendLog ?? throw new ArgumentNullException(nameof(appendLog));
        _pollInterval = pollInterval ?? DefaultPollInterval;
        if (_pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        _delay = delay ?? Task.Delay;
    }

    public async Task WaitUntilAvailableAsync(CancellationToken cancellationToken)
    {
        bool waiting = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_isAvailable()) break;
            if (!waiting)
            {
                _appendLog("WAITING FOR INTERNET | RECOVERY PAUSED");
                waiting = true;
            }
            await _delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (waiting)
            _appendLog("INTERNET RESTORED | RECOVERY RESUMING");
    }
}
