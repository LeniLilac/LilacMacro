using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows;

internal static class RobloxClientSizeStabilizer
{
    internal const int MaximumObservations = 4;
    internal const int ObservationIntervalMilliseconds = 100;

    public static async Task EnsureExpectedAsync(
        Func<PixelSize> observe,
        PixelSize expected,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observe);
        PixelSize[] observations = new PixelSize[MaximumObservations];
        for (int attempt = 0; attempt < observations.Length; attempt++)
        {
            PixelSize observed = observe();
            observations[attempt] = observed;
            if (observed == expected) return;
            if (attempt + 1 < observations.Length)
                await Task.Delay(ObservationIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Roblox remained outside {expected} after {operation}; observed " +
            string.Join(", ", observations.Select(value => value.ToString())) + ".");
    }
}
