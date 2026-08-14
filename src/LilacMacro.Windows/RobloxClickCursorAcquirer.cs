using System.Diagnostics;

namespace LilacMacro.Windows;

internal static class RobloxClickCursorAcquirer
{
    public static async Task<ClientBounds> AcquireAsync(
        ClientBounds client,
        Func<Task<ClientBounds>> prepareAgain,
        Action<ClientBounds> acquire,
        CancellationToken cancellationToken)
    {
        for (int cycle = 1; cycle <= RobloxInputProtocol.ClickCursorAcquisitionCycleCount; cycle++)
        {
            try
            {
                acquire(client);
                return client;
            }
            catch (RobloxPointerAcquisitionException) when (
                cycle < RobloxInputProtocol.ClickCursorAcquisitionCycleCount)
            {
                await Task.Delay(
                    RobloxInputProtocol.ClickCursorAcquisitionRetryMilliseconds,
                    cancellationToken).ConfigureAwait(false);
                client = await prepareAgain().ConfigureAwait(false);
            }
        }
        throw new UnreachableException();
    }
}
