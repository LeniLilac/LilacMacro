using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class DebugWorkflowCatalog
{
    public static readonly PixelSize ClientSize = new(1366, 700);

    private static readonly IReadOnlyList<OcrTargetRule> ResultSupportTargets =
    [
        new("Repeat Stage", "repeat stage", "repeat"),
        new("View Party", "view party", "party"),
        new("Game Stats", "game stats"),
        new("Gained Rewards", "gained rewards"),
        new("Clear Time", "clear time"),
        new("Total Yen", "total yen"),
        new("Total Kills", "total kills"),
        new("Total Damage", "total damage"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> LobbyTargets =
    [
        new("Store", "store"),
        new("Units", "units"),
        new("Items", "items"),
        new("Quests", "quests"),
        new("Summon", "summon"),
        new("Areas", "areas"),
        new("Play", "play"),
        new("Events", "events"),
    ];

    public static readonly IReadOnlyList<OcrTargetRule> ModeTargets =
    [
        new("Story", "story", "progressive gamemode", "progressive"),
        new("Raid", "raid", "difficult gamemode", "difficult"),
        new("Challenge", "challenge", "reward gamemode", "reward"),
        new("Expedition", "expedition", "special gamemode", "special"),
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
        .. ResultSupportTargets,
    ];

    public static readonly IReadOnlyList<OcrTargetRule> VictoryTargets =
    [
        new("Victory", "victory"),
        .. ResultSupportTargets,
    ];

    public static readonly DebugStateSpec Lobby = new(
        "LOBBY",
        Dataset("lobby-20260802-185951"),
        [1],
        2,
        LobbyTargets);

    public static readonly DebugStateSpec PlayUi = new(
        "PLAY UI",
        Dataset("play-ui-20260802-191143"),
        [1],
        2,
        ModeTargets);

    public static readonly DebugStateSpec EventSelect = new(
        "EVENT SELECT",
        Dataset("event-select-20260802-224426"),
        [1],
        3,
        EventSelectionRules.StateTargets,
        DebugMatchMode.ExactTargets);

    public static readonly DebugStateSpec AreasUi = new(
        "AREAS UI",
        Dataset("areas-ui-20260802-231943"),
        [1],
        3,
        AreaSelectionRules.StateTargets,
        DebugMatchMode.RequiredFirstTarget);

    public static readonly DebugStateSpec UnitInventory = new(
        "UNIT INVENTORY",
        Dataset("unit-inventory-detect-to-teams-swap-ui-20260802-222311"),
        [1],
        2,
        UnitInventoryTargets,
        DebugMatchMode.RequiredFirstTarget);

    public static readonly DebugStateSpec TeamSwap = new(
        "TEAM SWAP",
        Dataset("team-swap-20260802-222627"),
        [1],
        3,
        TeamSwapTargets);

    public static readonly DebugStateSpec TeamLoadConfirm = new(
        "TEAM LOAD CONFIRM",
        Dataset("team-swap-confirm-flow-20260802-223223"),
        [2],
        2,
        TeamLoadConfirmTargets);

    public static readonly DebugStateSpec TeamIncludeEquipment = new(
        "TEAM INCLUDE EQUIPMENT",
        Dataset("team-swap-confirm-flow-20260802-223223"),
        [3],
        3,
        TeamIncludeEquipmentTargets);

    public static readonly DebugStateSpec ChallengeTypePicker = new(
        "CHALLENGE TYPE",
        Dataset("challenge-type-picker-20260802-215826"),
        [1],
        3,
        ChallengeTypeTargets);

    public static readonly DebugStateSpec ChallengeAvailable = new(
        "CHALLENGE AVAILABLE",
        Dataset("challenge-set-1-20260807-002022"),
        [1],
        4,
        ChallengeAvailableTargets,
        DebugMatchMode.DeclarativeEvidence,
        RequiredTargetNames: ["Challenges", "Back", "Select Stage"],
        PoolTargetNames: MapTargets.Select(target => target.Name).ToArray(),
        MinimumPoolMatches: 1);

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
        SameRowTargetNames: ["Back", "Available In", "Enter Matchmaking"]);

    public static readonly DebugStateSpec StoryMap = new(
        "STORY MAP",
        Dataset("story-map-picker-20260802-192129"),
        [2],
        2,
        MapTargets);

    public static readonly DebugStateSpec RaidMap = new(
        "RAID MAP",
        Dataset("raid-map-picker-20260802-215104"),
        [1],
        1,
        RaidMapTargets);

    public static readonly DebugStateSpec ExpeditionMap = new(
        "EXPEDITION MAP",
        Dataset("expedition-map-picker-20260802-220435"),
        [1],
        3,
        ExpeditionMapTargets);

    public static readonly DebugStateSpec StoryActPicker = new(
        "STORY ACT",
        Dataset("story-map-act-picker-play-ui-20260802-193045"),
        [1],
        3,
        StoryActPickerTargets);

    public static readonly DebugStateSpec RaidActPicker = new(
        "RAID ACT",
        Dataset("raid-map-act-picker-20260802-215448"),
        [1],
        3,
        RaidActPickerTargets);

    public static readonly DebugStateSpec MatchPreview = new(
        "MATCH PREVIEW",
        Dataset("match-preview-general-20260802-211007"),
        [1],
        2,
        MatchPreviewTargets,
        DebugMatchMode.RequiredFirstTarget);

    public static readonly DebugStateSpec MatchPrestart = new(
        "MATCH PRESTART",
        Dataset("match-prestart-20260802-212342"),
        [1],
        2,
        MatchPrestartTargets,
        DebugMatchMode.RepeatedTarget);

    public static readonly DebugStateSpec Defeat = new(
        "DEFEAT",
        Dataset("defeat-screen-general-20260802-213156"),
        [1],
        3,
        DefeatTargets,
        DebugMatchMode.RequiredFirstTarget);

    public static readonly DebugStateSpec Victory = new(
        "VICTORY",
        Dataset("victory-screen-general-20260802-214302"),
        [1],
        3,
        VictoryTargets,
        DebugMatchMode.RequiredFirstTarget);

    public static readonly DebugStateSpec UnitPanel = new(
        "UNIT PANEL",
        Dataset("unit-selection-verification-20260806-180017"),
        [1],
        2,
        UnitPanelTargets);

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

    private static string Dataset(string directory) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "LilacMacro Datasets",
        directory);
}
