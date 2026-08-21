using LilacMacro.App.Debugging;
using LilacMacro.App.Views;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Placements;
using LilacMacro.Core.Automation;
using System.Text.RegularExpressions;

namespace LilacMacro.App.Runtime;

internal sealed class MacroTaskOptionsFactory(
    MacroOwnerState ownerState,
    PlacementSetupStore placements)
{
    private readonly ChallengePlacementResolver _challengePlacements = new(placements);
    private readonly TowerPlacementResolver _towerPlacements = new(placements);

    public async Task<StoryWireTestOptions> CreateAsync(
        PlanTaskPrototype task,
        string device,
        CancellationToken cancellationToken)
    {
        WireGameMode gameMode = task.Mode switch
        {
            PlanTaskMode.Raid => WireGameMode.Raid,
            PlanTaskMode.Challenge => WireGameMode.Challenge,
            PlanTaskMode.Expedition => WireGameMode.Expedition,
            PlanTaskMode.Event => WireGameMode.Event,
            PlanTaskMode.Tower => WireGameMode.Tower,
            _ => WireGameMode.Story,
        };
        TowerType towerType = gameMode == WireGameMode.Tower
            ? TowerRunPolicy.ParseType(task.Route)
            : TowerType.Trait;
        (string mapName, StoryAct act) = gameMode is WireGameMode.Challenge or WireGameMode.Tower
            ? ("AUTO", StoryAct.Act1)
            : ParseRoute(task.Route);
        if (gameMode == WireGameMode.Tower)
            await _towerPlacements.ValidateAsync(towerType, cancellationToken);
        int team = gameMode == WireGameMode.Challenge
            ? await _challengePlacements.ResolveCommonTeamAsync(cancellationToken)
            : gameMode == WireGameMode.Tower
                ? 1
                : await ResolveTeamAsync(gameMode, mapName, act, cancellationToken);
        MacroRuntimeKeySnapshot keys = ownerState.KeyBindings.Snapshot();
        if (gameMode == WireGameMode.Tower && keys.UnitInventory is null)
            throw new InvalidDataException("Tower tasks require a Unit inventory key binding for the in-match team swap.");
        RegularChallengeType[] challengeTypes = gameMode == WireGameMode.Challenge
            ? EnabledChallengeTypes(task)
            : [];
        return new StoryWireTestOptions(
            DebugEvidenceMode.ImageWithOcrFallback,
            gameMode,
            team,
            mapName,
            act,
            task.HardMode ? StoryDifficulty.Hard : StoryDifficulty.Normal,
            challengeTypes,
            new StoryWireNavigationKeys(keys.PlayMenu, keys.UnitInventory, keys.AreasMenu),
            keys.Placement,
            keys.ShiftLock,
            device,
            RunMatchRuntime: true,
            RepeatStage: false,
            ExpeditionDifficulty: task.Difficulty,
            InfiniteWave: task.InfiniteWave,
            BossesBeforeExtract: task.BossesBeforeExtract,
            ExtractAtCheckpoint: task.ExtractAtCheckpoint,
            ExpeditionRewardTarget: task.RewardTarget,
            TowerType: towerType,
            TowerGoalFloor: gameMode == WireGameMode.Tower ? task.Target : 0);
    }

    private async Task<int> ResolveTeamAsync(
        WireGameMode gameMode,
        string mapName,
        StoryAct act,
        CancellationToken cancellationToken)
    {
        string mapId = gameMode switch
        {
            WireGameMode.Raid => $"raid-spirit-city-{RouteId(act)}",
            WireGameMode.Expedition => $"expedition-{Slug(mapName)}",
            WireGameMode.Event => LilacMacro.Core.Automation.EventRunPolicy.MapId(mapName, act),
            _ => $"story-{Slug(mapName)}",
        };
        PlacementMapDefinition map = PlacementMapCatalog.Definitions.First(candidate => candidate.Id == mapId);
        PlacementSetupDocument document = await placements.LoadAsync(map.Id, cancellationToken);
        PlacementRouteDefinition definition = PlacementRouteCatalog.For(map)
            .FirstOrDefault(candidate => candidate.Id == RouteId(act))
            ?? PlacementRouteCatalog.For(map).First(candidate => candidate.IsShared);
        return PlacementRouteCatalog.EffectiveRoute(document, definition).TeamSlot;
    }

    private static RegularChallengeType[] EnabledChallengeTypes(PlanTaskPrototype task)
    {
        List<RegularChallengeType> types = [];
        if (task.RunTrait) types.Add(RegularChallengeType.Trait);
        if (task.RunStat) types.Add(RegularChallengeType.Stat);
        if (task.RunSprite) types.Add(RegularChallengeType.Sprite);
        if (types.Count == 0) throw new InvalidDataException("Challenge task has no enabled types.");
        return [.. types];
    }

    internal static (string Map, StoryAct Act) ParseRoute(string route)
    {
        if (!string.IsNullOrWhiteSpace(route))
        {
            Match routeMatch = Regex.Match(
                route,
                @"\b(Act\s+[1-5]|Infinite|Mastery)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (routeMatch.Success)
            {
                string parsedMap = route[..routeMatch.Index].Trim().TrimEnd('·', 'Â', '-', '|', '/').Trim();
                if (parsedMap.Length == 0) throw new InvalidDataException("Task route has no map.");
                string parsedAct = Regex.Replace(routeMatch.Value, @"\s+", " ").ToLowerInvariant();
                StoryAct parsedStoryAct = parsedAct switch
                {
                    "act 1" => StoryAct.Act1,
                    "act 2" => StoryAct.Act2,
                    "act 3" => StoryAct.Act3,
                    "act 4" => StoryAct.Act4,
                    "act 5" => StoryAct.Act5,
                    "infinite" => StoryAct.Infinite,
                    "mastery" => StoryAct.Mastery,
                    _ => throw new InvalidDataException("Task route act is invalid."),
                };
                return (parsedMap, parsedStoryAct);
            }
        }
        string[] parts = route.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim().TrimEnd('Â').Trim())
            .ToArray();
        string map = parts.FirstOrDefault() ?? throw new InvalidDataException("Task route has no map.");
        string actText = parts.FirstOrDefault(part => part.StartsWith("Act ", StringComparison.OrdinalIgnoreCase))
            ?? parts.FirstOrDefault(part => part.Equals("Infinite", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Mastery", StringComparison.OrdinalIgnoreCase))
            ?? "Act 1";
        StoryAct act = actText.ToLowerInvariant() switch
        {
            "act 1" => StoryAct.Act1,
            "act 2" => StoryAct.Act2,
            "act 3" => StoryAct.Act3,
            "act 4" => StoryAct.Act4,
            "act 5" => StoryAct.Act5,
            "infinite" => StoryAct.Infinite,
            "mastery" => StoryAct.Mastery,
            _ => throw new InvalidDataException("Task route act is invalid."),
        };
        return (map, act);
    }

    private static string RouteId(StoryAct act) => act switch
    {
        StoryAct.Act1 => "act-1",
        StoryAct.Act2 => "act-2",
        StoryAct.Act3 => "act-3",
        StoryAct.Act4 => "act-4",
        StoryAct.Act5 => "act-5",
        StoryAct.Infinite => "infinite",
        StoryAct.Mastery => "mastery",
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private static string Slug(string value) =>
        value.ToLowerInvariant().Replace("'", string.Empty).Replace(' ', '-');
}
