namespace LilacMacro.Core.Placements;

public sealed class StableUnitUpgradeTracker(int requiredStableObservations = 2)
{
    private UnitUpgradeState _candidate;
    private int _stable;

    public UnitUpgradeState Observe(UnitUpgradeState state)
    {
        if (state == UnitUpgradeState.Unknown)
        {
            _candidate = state;
            _stable = 0;
            return UnitUpgradeState.Unknown;
        }
        if (_candidate == state) _stable++;
        else
        {
            _candidate = state;
            _stable = 1;
        }
        return _stable >= requiredStableObservations ? state : UnitUpgradeState.Unknown;
    }
}
