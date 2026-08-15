using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class DebugWorkflowCatalog
{
    public static readonly PixelSize ClientSize = new(1366, 700);

    public static readonly OcrTargetRule EventSelectStageTarget =
        new("Select Stage", "select stage");

    public static readonly IReadOnlyList<OcrTargetRule> EventPageConfirmTargets =
    [
        new("Event Gamemode", "event gamemode"),
        new("Battlepass", "battlepass"),
        new("Quests", "quests"),
        new("Shop", "shop"),
        new("Summon", "summon"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> EventActTargets =
    [
        EventRunPolicy.TargetFor(StoryAct.Act1),
        EventRunPolicy.TargetFor(StoryAct.Act2),
        EventRunPolicy.TargetFor(StoryAct.Act3),
        EventRunPolicy.TargetFor(StoryAct.Act4),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> EventStagePreviewTargets =
    [
        .. EventActTargets,
        EventSelectStageTarget,
        new("Enter Matchmaking", "enter matchmaking", "matchmaking"),
        new("Stage Buffs", "stage buffs"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> UnitInventoryTargets =
    [
        new("Teams", "teams"),
        new("Inventory Action", "unequip all", "unequip", "quick sell", "quick"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> TeamSwapTargets =
    [
        new("Unit Teams", "unit teams"),
        new("Save Team", "save team", "save"),
        new("Load Team", "load team", "load"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> TeamLoadConfirmTargets =
    [
        new("Load Team", "load team"),
        new("Confirm", "confirm"),
        new("Cancel", "cancel"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> TeamSaveConfirmTargets =
    [
        new("Save Team", "save team"),
        new("Confirm", "confirm"),
        new("Cancel", "cancel"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> TeamIncludeEquipmentTargets =
    [
        new("Include", "include"),
        new("Exclude", "exclude"),
        new("Cancel", "cancel"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ChallengeTypeTargets =
    [
        new("Challenge", "challenges", "challenge"),
        new("Daily Challenge", "daily challenge"),
        new("Weekly Challenge", "weekly challenge"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> MapTargets =
    [
        new("School Grounds", "school", "school grounds", "grounds", "ground"),
        new("Flower Forest", "flower forest", "flower"),
        new("Rose Kingdom", "rose", "kingdom", "rose kingdom"),
        new("Fairy King Forest", "fairy king", "fairy king forest", "king forest"),
        new("King's Tomb", "kings tomb", "tomb"),
        new("East Town", "east town", "east", "town"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ChallengeAvailableTargets =
    [
        new("Challenges", "challenges"),
        .. MapTargets,
        new("Back", "back"),
        new("Select Stage", "select stage"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ChallengeCooldownTargets =
    [
        new("Challenges", "challenges"),
        .. MapTargets,
        new("Back", "back"),
        new("Available In", "available in"),
        new("Enter Matchmaking", "enter matchmaking", "matchmaking"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> RaidMapTargets =
    [
        new("Spirit City", "spirit city", "spirit", "city"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ExpeditionMapTargets =
    [
        new("School Grounds", "school", "grounds", "school grounds"),
        new("Flower Forest", "flower", "forest", "flower forest"),
        new("Rose Kingdom", "rose", "kingdom", "rose kingdom"),
        new("East Town", "east", "town", "east town"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> StoryActPickerTargets =
    [
        new("Story", "story"),
        new("Select Stage", "select stage"),
        new("Enter Matchmaking", "enter matchmaking", "matchmaking", "enter"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> RaidActPickerTargets =
    [
        new("Raid", "raid"),
        new("Select Stage", "select stage"),
        new("Enter Matchmaking", "enter matchmaking", "matchmaking", "enter"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> MatchPreviewTargets =
    [
        new("Start", "start"),
        new("Change Map", "change map", "change", "map"),
        new("Disband", "disband"),
        new("Invite Players", "invite players", "invite", "players"),
        new("Leave Party", "leave party", "leave", "party"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> MatchPrestartTargets =
    [
        new("Start Game", "start game"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> UnitPanelTargets =
    [
        new("Priority", "priority", "prlorlty", "priortty"),
        new("Sell", "sell"),
        new("DPS", "dps"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> DefeatTargets =
    [
        new("Defeat", "defeat"),
        .. DebugWorkflowTargets.ResultSupport,
    ];

    public static readonly IReadOnlyList<OcrTargetRule> VictoryTargets =
    [
        new("Victory", "victory"),
        .. DebugWorkflowTargets.ResultSupport,
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ExpeditionHubTargets =
    [
        new("Expedition Hub", "expedition hub", "expeditionhub"),
        new("Expedition Resources", "play and manage all expedition"),
        new("Buildings", "resources and buildings", "resources and buildin"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> RefuelPanelTargets =
    [
        new("Add Fuel", "add fuel", "addfuel"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> RefuelConfirmationTargets =
    [
        new("Confirm", "confirm"),
        new("Cancel", "cancel"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ShopAreaTargets =
    [
        new("Shop Areas", "shop areas"),
        new("Raid Shop", "raid shop", "raidshop"),
        new("Gold Shop", "gold shop", "goldshop"),
    ];

    public static readonly DebugStateSpec Lobby = new(
        "LOBBY",
        Dataset("lobby-20260802-185951"),
        [1],
        2,
        DebugWorkflowTargets.Lobby,
        RegionLabel: "Lobby State");

    public static readonly DebugStateSpec PlayUi = new(
        "PLAY UI",
        Dataset("play-ui-20260802-191143"),
        [1],
        2,
        DebugWorkflowTargets.Modes,
        RegionLabel: "Play UI State");

    public static readonly DebugStateSpec EventSelect = new(
        "EVENT SELECT",
        Dataset("event-select-20260802-224426"),
        [1],
        3,
        EventSelectionRules.StateTargets,
        DebugMatchMode.ExactTargets,
        RegionLabel: "Event Select State");

    public static readonly DebugStateSpec EventPageConfirm = new(
        "EVENT PAGE CONFIRM",
        Dataset("villain-invasion-set1-20260812-230831"),
        [3],
        3,
        EventPageConfirmTargets,
        DebugMatchMode.RequiredFirstTarget,
        RegionLabel: "Event Page Confirm + Event Gamemode button");

    public static readonly DebugStateSpec EventActPicker = new(
        "EVENT ACT PICKER",
        Dataset("villain-invasion-set1-20260812-230831"),
        [1, 2],
        1,
        EventActTargets,
        RegionLabel: "Act OCR");

    public static readonly DebugStateSpec EventStagePreview = new(
        "EVENT STAGE PREVIEW",
        Dataset("villain-invasion-set2-20260812-231832"),
        [1],
        3,
        EventStagePreviewTargets,
        RegionLabel: "Event Stage Preview State");

    public static readonly DebugStateSpec AreasUi = new(
        "AREAS UI",
        Dataset("areas-ui-20260802-231943"),
        [1],
        3,
        AreaSelectionRules.StateTargets,
        DebugMatchMode.RequiredFirstTarget,
        RegionLabel: "Areas UI State");

    public static readonly DebugStateSpec ExpeditionHub = new(
        "EXPEDITION HUB",
        Dataset("areas-ui-expedition-hub-20260812-184939"),
        [1],
        3,
        ExpeditionHubTargets,
        DebugMatchMode.RequiredFirstTarget,
        RegionLabel: "Expedition Hub State");

    public static readonly DebugStateSpec GoldMineRefuel = new(
        "GOLD MINE REFUEL",
        Dataset("new-gold-mine-20260814-125459"),
        [1, 2, 3],
        1,
        RefuelPanelTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Add Fuel"],
        RegionLabel: "Gold Mine Add Fuel State");

    public static readonly DebugStateSpec ResourceDrillRefuel = new(
        "RESOURCE DRILL REFUEL",
        Dataset("new-drill-refuel-20260814-125925"),
        [1, 2, 3],
        1,
        RefuelPanelTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Add Fuel"],
        RegionLabel: "Resource Drill Add Fuel State");

    public static readonly DebugStateSpec GoldMineRefuelConfirmation = new(
        "GOLD MINE REFUEL CONFIRMATION",
        Dataset("new-gold-mine-confirm-flow-20260814-125718"),
        [1, 2, 3],
        2,
        RefuelConfirmationTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Confirm", "Cancel"],
        SameRowTargetNames: ["Confirm", "Cancel"],
        RegionLabel: "Gold Mine Refuel Confirmation State");

    public static readonly DebugStateSpec ResourceDrillRefuelConfirmation = new(
        "RESOURCE DRILL REFUEL CONFIRMATION",
        Dataset("new-drill-refuel-confirm-flow-20260814-130600"),
        [1, 2, 3],
        2,
        RefuelConfirmationTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Confirm", "Cancel"],
        SameRowTargetNames: ["Confirm", "Cancel"],
        RegionLabel: "Resource Drill Refuel Confirmation State");

    public static readonly DebugStateSpec ShopAreas = new(
        "SHOP AREAS",
        Dataset("shop-areas-nav-for-gold-raid-shop-20260813-210616"),
        [1],
        3,
        ShopAreaTargets,
        RegionLabel: "Shop Areas State");

    public static readonly DebugStateSpec GoldShopSelector = new(
        "GOLD SHOP SELECTOR",
        Dataset("gold-shop-buy-part1-20260813-210659"),
        [1],
        2,
        DebugWorkflowTargets.GoldShopSelector,
        RegionLabel: "Shop Selection Buttons");

    public static readonly DebugStateSpec GoldShop = new(
        "GOLD SHOP",
        Dataset("gold-shop-buy-part1-20260813-210659"),
        [2],
        2,
        DebugWorkflowTargets.GoldShop,
        RegionLabel: "Gold Shop UI Confirm");

    public static readonly DebugStateSpec RaidShopSelector = new(
        "RAID SHOP SELECTOR",
        Dataset("raid-shop-buy-part1-20260813-213958"),
        [1],
        2,
        DebugWorkflowTargets.RaidShopSelector,
        RegionLabel: "Raid Shop Selector State");

    public static readonly DebugStateSpec RaidShop = new(
        "RAID SHOP",
        Dataset("raid-shop-buy-part2-20260813-214144"),
        [1],
        2,
        DebugWorkflowTargets.RaidShop,
        RegionLabel: "Raid Shop UI Confirm");

    public static readonly DebugStateSpec ExpeditionShopSelector = new(
        "EXPEDITION SHOP SELECTOR",
        Dataset("expedition-shop-ui-nav-20260814-003616"),
        [1],
        1,
        DebugWorkflowTargets.ExpeditionShopSelector,
        DebugMatchMode.ExactTargets,
        RegionLabel: "Expedition Shop Interaction Search");

    public static readonly DebugStateSpec ExpeditionShop = new(
        "EXPEDITION SHOP",
        Dataset("expediton-shop-scroll-multi-ui-scale-20260814-083856"),
        [1, 2, 3],
        1,
        DebugWorkflowTargets.ExpeditionShop,
        DebugMatchMode.ExactTargets,
        RegionLabel: "Confirm expedition shop is open");

    public static readonly DebugStateSpec ExpeditionShopPurchaseDialog = new(
        "EXPEDITION SHOP PURCHASE DIALOG",
        Dataset("expedition-shop-buy-flow-20260814-085142"),
        [1, 2, 3],
        3,
        DebugWorkflowTargets.ShopPurchaseDialog,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Buy Amount", "Purchase Question", "Cancel"],
        RegionLabel: "Shop Purchase Dialog State");

    public static readonly DebugStateSpec ShopPurchaseDialog = new(
        "SHOP PURCHASE DIALOG",
        Dataset("gold-shop-buy-part3-20260813-213107"),
        [1],
        3,
        DebugWorkflowTargets.ShopPurchaseDialog,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Buy Amount", "Purchase Question", "Cancel"],
        RegionLabel: "Shop Purchase Dialog State");

    public static readonly DebugStateSpec UnitInventory = new(
        "UNIT INVENTORY",
        Dataset("unit-inventory-detect-to-teams-swap-ui-20260802-222311"),
        [1],
        2,
        UnitInventoryTargets,
        DebugMatchMode.RequiredFirstTarget,
        RegionLabel: "Unit Inventory State");

    public static readonly DebugStateSpec TeamSwap = new(
        "TEAM SWAP",
        Dataset("team-swap-20260802-222627"),
        [1],
        3,
        TeamSwapTargets,
        RegionLabel: "Team Swap State");

    public static readonly DebugStateSpec TeamLoadConfirm = new(
        "TEAM LOAD CONFIRM",
        Dataset("team-swap-flow-revised-20260808-054531"),
        [2],
        3,
        TeamLoadConfirmTargets,
        RegionLabel: "Team Modal State");

    public static readonly DebugStateSpec TeamSaveConfirm = new(
        "TEAM SAVE CONFIRM",
        Dataset("team-swap-flow-revised-20260808-054531"),
        [1],
        3,
        TeamSaveConfirmTargets,
        RegionLabel: "Team Modal State");

    public static readonly DebugStateSpec TeamIncludeEquipment = new(
        "TEAM INCLUDE EQUIPMENT",
        Dataset("team-swap-flow-revised-20260808-054531"),
        [3],
        3,
        TeamIncludeEquipmentTargets,
        RegionLabel: "Team Modal State");

    public static readonly DebugStateSpec ChallengeTypePicker = new(
        "CHALLENGE TYPE",
        Dataset("challenge-type-picker-20260802-215826"),
        [1],
        3,
        ChallengeTypeTargets,
        RegionLabel: "Challenge Type State");

    public static readonly DebugStateSpec ChallengeAvailable = new(
        "CHALLENGE AVAILABLE",
        Dataset("challenge-set-1-20260807-002022"),
        [1],
        4,
        ChallengeAvailableTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Challenges", "Back", "Select Stage"],
        PoolTargetNames: MapTargets.Select(target => target.Name).ToArray(),
        MinimumPoolMatches: 1,
        RegionLabel: "Challenge Available State");

    public static readonly DebugStateSpec ChallengeCooldown = new(
        "CHALLENGE COOLDOWN",
        Dataset("challenge-set-3-20260807-003809"),
        [1],
        4,
        ChallengeCooldownTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Challenges", "Back", "Available In"],
        PoolTargetNames: MapTargets.Select(target => target.Name).ToArray(),
        MinimumPoolMatches: 1,
        FuzzyPrefixTargetNames: ["Available In"],
        SameRowTargetNames: ["Back", "Available In", "Enter Matchmaking"],
        RegionLabel: "Challenge Cooldown State");

    public static readonly DebugStateSpec StoryMap = new(
        "STORY MAP",
        Dataset("story-map-picker-20260802-192129"),
        [1],
        2,
        MapTargets,
        RegionLabel: "Story Map State");

    public static readonly DebugStateSpec RaidMap = new(
        "RAID MAP",
        Dataset("raid-map-picker-20260802-215104"),
        [1],
        1,
        RaidMapTargets,
        RegionLabel: "Raid Map State");

    public static readonly DebugStateSpec ExpeditionMap = new(
        "EXPEDITION MAP",
        Dataset("expedition-map-picker-20260802-220435"),
        [1],
        3,
        ExpeditionMapTargets,
        RegionLabel: "Expedition Map State");

    public static readonly DebugStateSpec StoryActPicker = new(
        "STORY ACT",
        Dataset("story-map-act-picker-play-ui-20260802-193045"),
        [1],
        3,
        StoryActPickerTargets,
        RegionLabel: "Story Act State");

    public static readonly DebugStateSpec RaidActPicker = new(
        "RAID ACT",
        Dataset("raid-map-act-picker-20260802-215448"),
        [1],
        3,
        RaidActPickerTargets,
        RegionLabel: "Raid Act State");

    public static readonly DebugStateSpec MatchPreview = new(
        "MATCH PREVIEW",
        Dataset("match-preview-general-20260802-211007"),
        [1],
        2,
        MatchPreviewTargets,
        DebugMatchMode.RequiredFirstTarget,
        RegionLabel: "Match Preview State");

    public static readonly DebugStateSpec MatchPrestart = new(
        "MATCH PRESTART",
        Dataset("new-start-game-button-20260814-082314"),
        [1, 2, 3],
        2,
        MatchPrestartTargets,
        DebugMatchMode.RepeatedTarget,
        RegionLabel: "match prestart");

    public static readonly DebugStateSpec Defeat = new(
        "DEFEAT",
        Dataset("defeat-screen-general-20260802-213156"),
        [1],
        3,
        DefeatTargets,
        DebugMatchMode.RequiredFirstTarget,
        RegionLabel: "Defeat State");

    public static readonly DebugStateSpec Victory = new(
        "VICTORY",
        Dataset("victory-screen-general-20260802-214302"),
        [1],
        3,
        VictoryTargets,
        DebugMatchMode.RequiredFirstTarget,
        RegionLabel: "Victory State");

    public static readonly DebugStateSpec UnitPanel = new(
        "UNIT PANEL",
        Dataset("unit-selection-verification-20260806-180017"),
        [1],
        2,
        UnitPanelTargets,
        RegionLabel: "Unit Panel State");

    public static OcrTargetRule ConfirmationFor(StoryAct act) => act switch
    {
        StoryAct.Act1 => new("Act 1", "act 1"),
        StoryAct.Act2 => new("Act 2", "act 2"),
        StoryAct.Act3 => new("Act 3", "act 3"),
        StoryAct.Act4 => new("Act 4", "act 4"),
        StoryAct.Act5 => new("Act 5", "act 5"),
        StoryAct.Infinite => new("Infinite", "infinite"),
        StoryAct.Mastery => new("Mastery", "mastery"),
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private static string Dataset(string directory) => RuntimeEvidenceDatasetCatalog.Dataset(directory);
}
