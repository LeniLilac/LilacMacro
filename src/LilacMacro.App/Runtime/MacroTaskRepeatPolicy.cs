using LilacMacro.App.Views;

namespace LilacMacro.App.Runtime;

internal static class MacroTaskRepeatPolicy
{
    public static bool Supports(PlanTaskMode mode) => mode is
        PlanTaskMode.Story or
        PlanTaskMode.Raid or
        PlanTaskMode.Expedition or
        PlanTaskMode.Event;
}

internal sealed class MacroRunTeamState
{
    public int? LoadedTeam { get; private set; }

    public bool CanReuse(int requestedTeam) => LoadedTeam == requestedTeam;

    public void MarkLoaded(int team) => LoadedTeam = team;
}
