using System.Diagnostics;
using LilacMacro.Windows;

namespace LilacMacro.App.Workspace;

internal sealed class RobloxWindowAcquisitionWaiter(
    Func<RobloxWindowAcquisition> observe,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    internal const int MaximumAttempts = 13;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<RobloxWindowAcquisition> RunAsync(
        bool waitForCapturable,
        Action<int, RobloxWindowAcquisition> observed,
        CancellationToken cancellationToken)
    {
        int attemptLimit = waitForCapturable ? MaximumAttempts : 1;
        for (int attempt = 1; attempt <= attemptLimit; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RobloxWindowAcquisition result = observe();
            observed(attempt, result);
            if (result.Succeeded || attempt == attemptLimit) return result;
            await _delay(RetryDelay, cancellationToken);
        }

        throw new UnreachableException();
    }
}
