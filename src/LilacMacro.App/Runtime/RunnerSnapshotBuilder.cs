using System.Reflection;
using System.Text.Json;
using LilacMacro.App.Debugging;
using LilacMacro.App.Views;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.App.Runtime;

internal sealed class RunnerSnapshotBuilder
{
    private readonly DebugStateDatasetContextLoader _contexts = new();

    public async Task<RunnerRuntimeSnapshot> BuildAsync(
        MacroOwnerState ownerState,
        PlanPrototype plan,
        string ownerSid,
        string appVersion,
        long revision,
        JsonElement placementSetups,
        CancellationToken cancellationToken)
    {
        RunnerTaskSnapshot[] tasks = MacroPriorityPolicy.Flatten(plan)
            .Where(MacroPriorityPolicy.Supported)
            .Select((task, index) => CreateTask(task, index))
            .ToArray();
        PlanTaskPrototype[] unsupported = MacroPriorityPolicy.Flatten(plan)
            .Where(task => !MacroPriorityPolicy.Supported(task))
            .ToArray();
        if (unsupported.Length > 0)
        {
            string modes = string.Join(", ", unsupported.Select(task => task.ModeLabel).Distinct(StringComparer.Ordinal));
            throw new InvalidOperationException($"The local runner cannot publish unsupported plan modes: {modes}.");
        }

        return new RunnerRuntimeSnapshot
        {
            Revision = revision,
            AppVersion = appVersion,
            OwnerSid = ownerSid,
            PlanName = plan.Name,
            Tasks = tasks,
            PlacementSetups = placementSetups,
            StateContexts = await LoadContextsAsync(cancellationToken).ConfigureAwait(false),
            KeyBindings = ownerState.KeyBindings.CreatePersistedSnapshot(),
        };
    }

    private async Task<IReadOnlyList<RunnerStateContextSnapshot>> LoadContextsAsync(CancellationToken cancellationToken)
    {
        List<RunnerStateContextSnapshot> snapshots = [];
        foreach (DebugStateSpec state in StateSpecs())
        {
            DebugStateDatasetContext context = await _contexts.LoadAsync(state, cancellationToken).ConfigureAwait(false);
            snapshots.Add(new RunnerStateContextSnapshot(
                state.Name,
                context.RegionOfInterest,
                context.VisualAnchors.Select(anchor => new RunnerVisualAnchorSnapshot(
                    anchor.Text,
                    anchor.MatchMode,
                    anchor.SpatialSelector,
                    anchor.SpatialAnchorText)).ToArray()));
        }
        return snapshots;
    }

    private static IEnumerable<DebugStateSpec> StateSpecs() => StateSpecs(typeof(DebugWorkflowCatalog))
        .Concat(TowerWorkflowCatalog.All())
        .Concat(ExpeditionCheckpointStateCatalog.All())
        .OrderBy(state => state.Name, StringComparer.Ordinal);

    private static IEnumerable<DebugStateSpec> StateSpecs(Type catalog) => catalog
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(DebugStateSpec))
        .Select(field => (DebugStateSpec?)field.GetValue(null))
        .Where(state => state is not null)
        .Cast<DebugStateSpec>();

    private static RunnerTaskSnapshot CreateTask(PlanTaskPrototype task, int index) => new()
    {
        Id = $"task-{index + 1:D3}",
        Priority = task.Priority,
        Mode = task.Mode switch
        {
            PlanTaskMode.Story => RunnerTaskMode.Story,
            PlanTaskMode.Raid => RunnerTaskMode.Raid,
            PlanTaskMode.Challenge => RunnerTaskMode.Challenge,
            PlanTaskMode.Expedition => RunnerTaskMode.Expedition,
            PlanTaskMode.Event => RunnerTaskMode.Event,
            PlanTaskMode.Tower => RunnerTaskMode.Tower,
            PlanTaskMode.Utilities => RunnerTaskMode.Utilities,
            _ => throw new InvalidOperationException($"Unsupported local runner mode: {task.ModeLabel}."),
        },
        Route = task.Route,
        Target = task.Target,
        DefeatRetries = task.DefeatRetries,
        HardMode = task.HardMode,
        RunTrait = task.RunTrait,
        RunStat = task.RunStat,
        RunSprite = task.RunSprite,
        Difficulty = task.Difficulty,
        InfiniteWave = task.InfiniteWave,
        BossesBeforeExtract = task.BossesBeforeExtract,
        ExtractAtCheckpoint = task.ExtractAtCheckpoint,
        RewardTarget = task.RewardTarget,
        ShopItemIds = task.ShopItemIds.ToArray(),
    };
}
