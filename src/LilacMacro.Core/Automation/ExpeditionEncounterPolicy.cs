namespace LilacMacro.Core.Automation;

public readonly record struct ExpeditionEncounterMovement(int ForwardMilliseconds, int RightMilliseconds);

public static class ExpeditionEncounterPolicy
{
    public const int MaximumInteractionAttempts = 3;
    public const int TravelDelayMilliseconds = 15_000;

    public static ExpeditionEncounterMovement ForMap(string map) => map.Trim() switch
    {
        "School Grounds" => new(350, 700),
        "Flower Forest" => new(350, 700),
        "Rose Kingdom" => new(1000, 700),
        "East Town" => new(700, 700),
        _ => throw new InvalidDataException($"Unknown Expedition encounter map '{map}'."),
    };
}
