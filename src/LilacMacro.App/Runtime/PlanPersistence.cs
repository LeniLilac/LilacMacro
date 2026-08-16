using System.Collections.ObjectModel;
using LilacMacro.App.Views;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed record PlanSettingsSnapshot
{
    public string Name { get; init; } = string.Empty;

    public List<PlanBlockSettingsSnapshot> Blocks { get; init; } = [];
}

internal sealed record PlanBlockSettingsSnapshot
{
    public string Kind { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string Route { get; init; } = string.Empty;

    public int Target { get; init; }

    public int DefeatRetries { get; init; }

    public int Difficulty { get; init; } = 1;

    public int InfiniteWave { get; init; } = 140;

    public int BossesBeforeExtract { get; init; } = 1;

    public bool ExtractAtCheckpoint { get; init; } = true;

    public string RewardTarget { get; init; } = "None";

    public bool HardMode { get; init; }

    public bool RunTrait { get; init; } = true;

    public bool RunStat { get; init; } = true;

    public bool RunSprite { get; init; } = true;

    public List<string> ShopItemIds { get; init; } = [];

    public string Label { get; init; } = string.Empty;

    public bool Forever { get; init; } = true;

    public int RepeatCount { get; init; } = 2;

    public List<PlanBlockSettingsSnapshot> Children { get; init; } = [];
}

internal static class PlanPersistence
{
    private const string TaskKind = "task";
    private const string LoopKind = "loop";
    private const int MaximumPlans = 100;
    private const int MaximumBlocks = 1000;
    private const int MaximumDepth = 8;
    private const int MaximumNameLength = 100;
    private const int MaximumRouteLength = 200;

    public static List<PlanSettingsSnapshot> CreateSnapshot(IEnumerable<PlanPrototype> plans) =>
        plans.Select(plan => new PlanSettingsSnapshot
        {
            Name = plan.Name,
            Blocks = plan.Blocks.Select(CreateBlockSnapshot).ToList(),
        }).ToList();

    public static bool TryRestore(
        IReadOnlyList<PlanSettingsSnapshot>? snapshots,
        out ObservableCollection<PlanPrototype> plans)
    {
        plans = [];
        if (snapshots is null || snapshots.Count is < 1 or > MaximumPlans) return false;
        int blockCount = 0;
        foreach (PlanSettingsSnapshot? snapshot in snapshots)
        {
            if (snapshot is null || snapshot.Blocks is null || !ValidText(snapshot.Name, MaximumNameLength))
                return false;
            List<PlanBlockPrototype> blocks = [];
            foreach (PlanBlockSettingsSnapshot block in snapshot.Blocks)
            {
                if (!TryRestoreBlock(block, depth: 0, ref blockCount, out PlanBlockPrototype? restored))
                    return false;
                blocks.Add(restored);
            }
            Reindex(blocks);
            plans.Add(new PlanPrototype(snapshot.Name, blocks));
        }
        return true;
    }

    private static PlanBlockSettingsSnapshot CreateBlockSnapshot(PlanBlockPrototype block) => block switch
    {
        PlanTaskPrototype task => new PlanBlockSettingsSnapshot
        {
            Kind = TaskKind,
            Mode = task.Mode.ToString(),
            Route = task.Route,
            Target = task.Target,
            DefeatRetries = task.DefeatRetries,
            Difficulty = task.Difficulty,
            InfiniteWave = task.InfiniteWave,
            BossesBeforeExtract = task.BossesBeforeExtract,
            ExtractAtCheckpoint = task.ExtractAtCheckpoint,
            RewardTarget = task.RewardTarget,
            HardMode = task.HardMode,
            RunTrait = task.RunTrait,
            RunStat = task.RunStat,
            RunSprite = task.RunSprite,
            ShopItemIds = task.ShopItemIds.ToList(),
        },
        PlanLoopPrototype loop => new PlanBlockSettingsSnapshot
        {
            Kind = LoopKind,
            Label = loop.Label,
            Forever = loop.Forever,
            RepeatCount = loop.RepeatCount,
            Children = loop.Children.Select(CreateBlockSnapshot).ToList(),
        },
        _ => throw new InvalidDataException($"Unsupported plan block: {block.GetType().Name}.")
    };

    private static bool TryRestoreBlock(
        PlanBlockSettingsSnapshot? snapshot,
        int depth,
        ref int blockCount,
        out PlanBlockPrototype restored)
    {
        restored = null!;
        if (snapshot is null || snapshot.Children is null || snapshot.ShopItemIds is null || depth > MaximumDepth || ++blockCount > MaximumBlocks)
            return false;
        if (string.Equals(snapshot.Kind, TaskKind, StringComparison.Ordinal))
        {
            if (!Enum.TryParse(snapshot.Mode, ignoreCase: false, out PlanTaskMode mode) ||
                !Enum.IsDefined(mode) ||
                !ValidText(snapshot.Route, MaximumRouteLength) ||
                snapshot.Target < 1 ||
                snapshot.DefeatRetries is < 0 or > 20 ||
                snapshot.Difficulty is < 1 or > 3 ||
                snapshot.InfiniteWave is < 1 or > 999 ||
                snapshot.BossesBeforeExtract is < 0 or > 99 ||
                snapshot.Children.Count != 0)
            {
                return false;
            }
            if (mode == PlanTaskMode.Expedition)
            {
                try { _ = ExpeditionRewardPolicy.ParseResource(snapshot.RewardTarget); }
                catch (InvalidDataException) { return false; }
            }
            if (mode == PlanTaskMode.Utilities)
            {
                try { UtilityTaskPolicy.Validate(snapshot.Route, snapshot.ShopItemIds); }
                catch (Exception error) when (error is InvalidDataException or ArgumentException) { return false; }
            }
            restored = new PlanTaskPrototype
            {
                Mode = mode,
                Route = snapshot.Route,
                Target = snapshot.Target,
                DefeatRetries = snapshot.DefeatRetries,
                Difficulty = snapshot.Difficulty,
                InfiniteWave = snapshot.InfiniteWave,
                BossesBeforeExtract = snapshot.BossesBeforeExtract,
                ExtractAtCheckpoint = snapshot.ExtractAtCheckpoint,
                RewardTarget = snapshot.RewardTarget,
                HardMode = snapshot.HardMode,
                RunTrait = snapshot.RunTrait,
                RunStat = snapshot.RunStat,
                RunSprite = snapshot.RunSprite,
                ShopItemIds = snapshot.ShopItemIds.ToArray(),
            };
            return true;
        }

        if (!string.Equals(snapshot.Kind, LoopKind, StringComparison.Ordinal) ||
            !ValidText(snapshot.Label, MaximumNameLength) ||
            snapshot.RepeatCount is < 1 or > 100000)
        {
            return false;
        }
        PlanLoopPrototype loop = new()
        {
            Label = snapshot.Label,
            Forever = snapshot.Forever,
            RepeatCount = snapshot.RepeatCount,
        };
        foreach (PlanBlockSettingsSnapshot child in snapshot.Children)
        {
            if (!TryRestoreBlock(child, depth + 1, ref blockCount, out PlanBlockPrototype? restoredChild))
                return false;
            loop.Children.Add(restoredChild);
        }
        restored = loop;
        return true;
    }

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static void Reindex(IEnumerable<PlanBlockPrototype> blocks)
    {
        int priority = 1;
        foreach (PlanTaskPrototype task in Flatten(blocks).OfType<PlanTaskPrototype>())
            task.Priority = priority++;
    }

    private static IEnumerable<PlanBlockPrototype> Flatten(IEnumerable<PlanBlockPrototype> blocks)
    {
        foreach (PlanBlockPrototype block in blocks)
        {
            yield return block;
            if (block is PlanLoopPrototype loop)
                foreach (PlanBlockPrototype child in Flatten(loop.Children)) yield return child;
        }
    }
}
