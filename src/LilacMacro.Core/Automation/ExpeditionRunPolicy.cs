namespace LilacMacro.Core.Automation;

public enum ExpeditionNodeType
{
    Defense,
    Elite,
    Assault,
    Boss,
    Encounter,
    Checkpoint,
}
public enum ExpeditionNodeAction
{
    Wait,
    ReplayPlacementsAndStart,
    RunEncounter,
    Continue,
    Extract,
}

public static class ExpeditionStartGamePolicy
{
    public static readonly TimeSpan RetryWindow = MatchLoadPolicy.RetryWindow;
    public const int RetryMilliseconds = MatchLoadPolicy.RetryMilliseconds;

    public static bool IsWithinRetryWindow(TimeSpan elapsed) =>
        MatchLoadPolicy.IsWithinRetryWindow(elapsed);

    public static TimeSpan RetryDelay(TimeSpan elapsed) =>
        MatchLoadPolicy.RetryDelay(elapsed);
}

public static class ExpeditionDefenseStartPolicy
{
    public const int ArrivalMaximumObservations = 240;
    public const int ArrivalRetryMilliseconds = 500;
}

public static class ExpeditionNodeArrivalPolicy
{
    public const int MaximumObservations = 240;
    public const int RetryMilliseconds = 500;
}

public enum ExpeditionLiveControl
{
    None,
    Checkpoint,
    Encounter,
}

public static class ExpeditionLiveControlPolicy
{
    public const int ProbeIntervalMilliseconds = 2_000;

    public static ExpeditionLiveControl Select(bool checkpointAvailable, bool encounterAvailable) =>
        checkpointAvailable
            ? ExpeditionLiveControl.Checkpoint
            : encounterAvailable
                ? ExpeditionLiveControl.Encounter
                : ExpeditionLiveControl.None;

    public static bool RequiresLiveControlEvidence(ExpeditionNodeType node) =>
        node is ExpeditionNodeType.Defense or ExpeditionNodeType.Elite or
            ExpeditionNodeType.Encounter or ExpeditionNodeType.Checkpoint;
}

public static class ExpeditionProgressPolicy
{
    public static readonly TimeSpan MaximumSilence = TimeSpan.FromMinutes(5);

    public static bool HasStalled(TimeSpan elapsedSinceProgress) =>
        elapsedSinceProgress >= MaximumSilence;
}

public sealed class ExpeditionDefenseStartEpisodeTracker
{
    private bool _handled;

    public bool Observe(bool startGameVisible)
    {
        if (!startGameVisible)
        {
            _handled = false;
            return false;
        }

        return !_handled;
    }

    public void MarkHandled() => _handled = true;
}

public sealed class ExpeditionRunTracker(bool extractAtCheckpoint, int bossesBeforeExtract)
{
    private ExpeditionNodeType? _previous;
    private ExpeditionNodeAction? _lastCheckpointAction;

    public int RealBossesCompleted { get; private set; }

    public ExpeditionNodeAction Observe(ExpeditionNodeType node)
    {
        if (_previous == node) return ExpeditionNodeAction.Wait;
        bool completedRealBoss = _previous == ExpeditionNodeType.Boss && node == ExpeditionNodeType.Checkpoint;
        if (completedRealBoss) RealBossesCompleted++;
        _previous = node;
        return node switch
        {
            ExpeditionNodeType.Defense or ExpeditionNodeType.Elite =>
                ExpeditionNodeAction.ReplayPlacementsAndStart,
            ExpeditionNodeType.Encounter => ExpeditionNodeAction.RunEncounter,
            ExpeditionNodeType.Checkpoint when extractAtCheckpoint && completedRealBoss &&
                RealBossesCompleted >= Math.Max(0, bossesBeforeExtract) => ExpeditionNodeAction.Extract,
            ExpeditionNodeType.Checkpoint => ExpeditionNodeAction.Continue,
            _ => ExpeditionNodeAction.Wait,
        };
    }

    public ExpeditionNodeAction ObserveCheckpointSource()
    {
        ExpeditionNodeAction action = Observe(ExpeditionNodeType.Checkpoint);
        if (action != ExpeditionNodeAction.Wait)
        {
            _lastCheckpointAction = action;
            return action;
        }

        return _lastCheckpointAction ?? ExpeditionNodeAction.Continue;
    }
}
