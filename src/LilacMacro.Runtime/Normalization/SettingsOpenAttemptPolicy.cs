namespace LilacMacro.Runtime.Normalization;

internal static class SettingsOpenAttemptPolicy
{
    private static readonly int[] EarliestObservationByAttempt = [0, 2, 6, 12];

    public static bool ShouldAttempt(int observation, int completedAttempts) =>
        completedAttempts >= 0 &&
        completedAttempts < EarliestObservationByAttempt.Length &&
        observation >= EarliestObservationByAttempt[completedAttempts];
}
