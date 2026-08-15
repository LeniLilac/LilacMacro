namespace LilacMacro.Core.Automation;

public static class UtilityRespawnPolicy
{
    public const int MaximumAreasCleanupAttempts = 2;
    public const int ObservationsBetweenAreasCleanupAttempts = 6;
    public const int RequiredSettledLobbyObservations = 2;

    public static IReadOnlyList<int> CreateKeyOrder(int areasMenuVirtualKey)
    {
        if (!KeyboardKey.IsSupportedAutomationKey(areasMenuVirtualKey))
            throw new InvalidDataException("Areas menu must have a supported key.");

        return
        [
            areasMenuVirtualKey,
            KeyboardKey.Escape,
            'R',
            KeyboardKey.Enter,
        ];
    }

    public static int UpdateSettledLobbyObservations(
        int consecutiveObservations,
        bool lobbyObserved,
        bool areasObserved)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveObservations);
        return lobbyObserved && !areasObserved
            ? checked(consecutiveObservations + 1)
            : 0;
    }

    public static bool ShouldCloseAreas(
        bool areasObserved,
        int cleanupAttempts,
        int observationsSinceCleanup)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cleanupAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(observationsSinceCleanup);
        return areasObserved &&
               cleanupAttempts < MaximumAreasCleanupAttempts &&
               observationsSinceCleanup >= ObservationsBetweenAreasCleanupAttempts;
    }
}
