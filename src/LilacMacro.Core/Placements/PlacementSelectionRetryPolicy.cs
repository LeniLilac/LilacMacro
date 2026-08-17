namespace LilacMacro.Core.Placements;

public static class PlacementSelectionRetryPolicy
{
    public const int MaximumAttempts = 3;

    public static bool ShouldRetry(int attempt)
    {
        if (attempt is < 1 or > MaximumAttempts)
            throw new ArgumentOutOfRangeException(nameof(attempt));

        return attempt < MaximumAttempts;
    }
}
