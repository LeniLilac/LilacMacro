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

public sealed class ExpeditionRunTracker(bool extractAtCheckpoint, int bossesBeforeExtract)
{
    private ExpeditionNodeType? _previous;

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
}
