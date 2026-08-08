namespace LilacMacro.Core.Placements;

public sealed record PlacementPlaybackPlan(
    IReadOnlyList<PlacementStep> BeforeStart,
    PlacementStep StartGame,
    IReadOnlyList<PlacementStep> AfterStart)
{
    public static PlacementPlaybackPlan Create(PlacementRouteSetup route)
    {
        ArgumentNullException.ThrowIfNull(route);
        int boundary = route.Steps.FindIndex(step => step.Kind == PlacementStepKind.StartGame);
        if (boundary < 0 || route.Steps.FindLastIndex(step => step.Kind == PlacementStepKind.StartGame) != boundary)
        {
            throw new InvalidDataException("Placement playback requires exactly one Start Game boundary.");
        }

        return new PlacementPlaybackPlan(
            route.Steps.Take(boundary).Select(CopyStep).ToArray(),
            CopyStep(route.Steps[boundary]),
            route.Steps.Skip(boundary + 1).Select(CopyStep).ToArray());
    }

    public static IReadOnlyList<PlacementPlaybackGroup> Group(IReadOnlyList<PlacementStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        List<PlacementPlaybackGroup> groups = [];
        for (int index = 0; index < steps.Count;)
        {
            PlacementStepKind kind = steps[index].Kind;
            int count = kind == PlacementStepKind.Place
                ? steps.Skip(index).TakeWhile(step => step.Kind == PlacementStepKind.Place).Count()
                : 1;
            groups.Add(new PlacementPlaybackGroup(kind, steps.Skip(index).Take(count).ToArray()));
            index += count;
        }
        return groups;
    }

    private static PlacementStep CopyStep(PlacementStep step) => step with { };
}

public sealed record PlacementPlaybackGroup(
    PlacementStepKind Kind,
    IReadOnlyList<PlacementStep> Steps);
