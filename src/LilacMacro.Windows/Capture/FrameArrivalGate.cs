using System.Diagnostics;

namespace LilacMacro.Windows.Capture;

internal sealed class FrameArrivalGate : IDisposable
{
    private readonly AutoResetEvent _ready = new(false);
    private long _generation;
    private int _disposed;

    public long Generation => Volatile.Read(ref _generation);

    public void Notify()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _generation);
        try
        {
            _ready.Set();
        }
        catch (ObjectDisposedException)
        {
            // Disposal can race an already dispatched frame callback.
        }
    }

    public bool WaitForGeneration(long targetGeneration, int timeoutMilliseconds)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (Generation < targetGeneration)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            int remaining = timeoutMilliseconds - checked((int)Math.Min(int.MaxValue, elapsed.ElapsedMilliseconds));
            if (remaining <= 0) return false;
            try
            {
                _ready.WaitOne(remaining);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
        return true;
    }

    public void Wake()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            _ready.Set();
        }
        catch (ObjectDisposedException)
        {
            // Disposal can race a capture-session wakeup.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _ready.Set();
        _ready.Dispose();
    }
}
