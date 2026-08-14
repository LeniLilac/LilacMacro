using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Automation;

public static class EventRunPolicy
{
    public const string VillainInvasion = "Villain Invasion";

    public static bool RequiresActScroll(StoryAct act) => act is StoryAct.Act3 or StoryAct.Act4;

    public static string MapId(string map, StoryAct act)
    {
        if (!string.Equals(map, VillainInvasion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Event route '{map}' is not implemented.");
        if (act is < StoryAct.Act1 or > StoryAct.Act4)
            throw new InvalidDataException("Villain Invasion supports Acts 1 through 4.");
        return $"event-villain-invasion-{RouteId(act)}";
    }

    public static OcrTargetRule TargetFor(StoryAct act) => act switch
    {
        StoryAct.Act1 => new("Act 1", "act 1", "act1 death", "act 1 death"),
        StoryAct.Act2 => new("Act 2", "act 2", "act2 tartaros", "act 2 tartaros"),
        StoryAct.Act3 => new("Act 3", "act 3", "act3 sword", "act 3 sword"),
        StoryAct.Act4 => new("Act 4", "act 4", "crow dawn", "crow-dawn"),
        _ => throw new InvalidDataException("Villain Invasion supports Acts 1 through 4."),
    };

    private static string RouteId(StoryAct act) => $"act-{(int)act + 1}";
}
