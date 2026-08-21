namespace LilacMacro.Core.Automation;

public static class MatchTaskProgressPolicy
{
    public static void Apply<TKey>(
        TKey task,
        string taskName,
        bool isTower,
        bool victory,
        int verifiedTowerFloor,
        int defeatLimit,
        IDictionary<TKey, int> victories,
        IDictionary<TKey, int> defeats)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentNullException.ThrowIfNull(victories);
        ArgumentNullException.ThrowIfNull(defeats);

        if (isTower)
        {
            TowerTerminalState state = TowerRunPolicy.ApplyTerminalOutcome(
                victory,
                Current(victories, task),
                Current(defeats, task),
                verifiedTowerFloor,
                defeatLimit);
            victories[task] = state.Progress;
            if (state.DefeatsOnFloor == 0) defeats.Remove(task);
            else defeats[task] = state.DefeatsOnFloor;
            if (state.ShouldStop)
                throw new InvalidOperationException(
                    $"{taskName} reached its {defeatLimit}-defeat stop limit on floor {verifiedTowerFloor}.");
            return;
        }

        if (victory)
        {
            victories[task] = Current(victories, task) + 1;
            return;
        }

        defeats[task] = Current(defeats, task) + 1;
        if (defeats[task] > defeatLimit)
            throw new InvalidOperationException($"{taskName} exceeded its defeat retry limit.");
    }

    private static int Current<TKey>(IDictionary<TKey, int> values, TKey key)
        where TKey : notnull => values.TryGetValue(key, out int value) ? value : 0;
}
