namespace LilacMacro.Core.Placements;

public enum PlacementStepKind
{
    Place,
    Reconfigure,
    Delay,
    Upgrade,
    StartGame,
    Sell,
}

public enum PlacementTargetingPriority
{
    First,
    Last,
    Closest,
    Strongest,
    Boss,
    Weakest,
    Shielded,
    Fastest,
    None,
}

public enum PlacementAutoUpgradePriority
{
    Off,
    Priority1,
    Priority2,
    Priority3,
    Priority4,
    Priority5,
    Priority6,
}

public enum PlacementAutoUpgradeAction
{
    NoChange,
    Disable,
    Priority1,
    Priority2,
    Priority3,
    Priority4,
    Priority5,
    Priority6,
}
