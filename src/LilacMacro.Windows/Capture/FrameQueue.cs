namespace LilacMacro.Windows.Capture;

internal static class FrameQueue
{
    public static void DiscardAll<T>(Func<T?> tryTake)
        where T : class, IDisposable
    {
        using T? ignored = TakeLatest(tryTake);
    }

    public static T? TakeLatest<T>(Func<T?> tryTake)
        where T : class, IDisposable
    {
        T? latest = null;
        while (tryTake() is { } next)
        {
            latest?.Dispose();
            latest = next;
        }
        return latest;
    }
}
