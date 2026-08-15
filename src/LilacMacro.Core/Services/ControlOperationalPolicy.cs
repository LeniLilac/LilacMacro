namespace LilacMacro.Core.Services;

public static class ControlOperationalPolicy
{
    public static bool IsGameAvailable(SignedControlSnapshot? snapshot) =>
        snapshot?.Payload.Game.Available is not false;

    public static string GameUnavailableMessage(SignedControlSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Payload.Game.Available)
            throw new InvalidOperationException("The control snapshot reports the game as available.");
        return snapshot.Payload.Game.Message ?? "Anime Expeditions is temporarily unavailable.";
    }

    public static bool IsFeatureEnabled(
        SignedControlSnapshot? snapshot,
        string feature,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        if (!ControlFeatureIds.All.Contains(feature))
            throw new ArgumentOutOfRangeException(nameof(feature));
        return snapshot?.Payload.Disablements.Any(item =>
            string.Equals(item.Feature, feature, StringComparison.Ordinal) &&
            (item.ExpiresAt is null || item.ExpiresAt > now)) is not true;
    }

    public static IReadOnlyList<string> ActiveCodes(
        SignedControlSnapshot? snapshot,
        DateTimeOffset now) => snapshot?.Payload.Codes
        .Where(code => code.ExpiresAt is null || code.ExpiresAt > now)
        .Select(code => code.Code)
        .ToArray() ?? [];

    public static ControlSchedule? Schedule(
        SignedControlSnapshot? snapshot,
        string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!ControlScheduleKeys.All.Contains(key))
            throw new ArgumentOutOfRangeException(nameof(key));
        return snapshot?.Payload.Schedules.SingleOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));
    }

    public static DateTimeOffset NextScheduledOccurrence(
        ControlSchedule schedule,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!ControlScheduleKeys.All.Contains(schedule.Key) || schedule.CadenceSeconds < 1)
            throw new InvalidDataException("The control schedule was invalid.");
        if (completedAt < schedule.NextAt) return schedule.NextAt;
        long elapsedSeconds = checked((long)Math.Floor(
            (completedAt - schedule.NextAt).TotalSeconds));
        long periods = checked(elapsedSeconds / schedule.CadenceSeconds + 1);
        return schedule.NextAt.AddSeconds(checked(periods * (long)schedule.CadenceSeconds));
    }
}
