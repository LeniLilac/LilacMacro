using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class OcrRuleEngineTests
{
    [Theory]
    [InlineData("$ ProGresSive Gamemode!", "progressivegamemode")]
    [InlineData("gam em ode", "gamemode")]
    [InlineData("King's Tomb", "kingstomb")]
    [InlineData("FÃ©e Store_42", "festore42")]
    public void Normalize_KeepsOnlyAsciiLettersAndDigits(string value, string expected)
    {
        Assert.Equal(expected, OcrRuleEngine.Normalize(value));
    }

    [Fact]
    public void Evaluate_RequiresDistinctLobbyTargets()
    {
        OcrTargetRule[] targets =
        [
            new("Store", "store"),
            new("Play", "play"),
            new("Events", "events"),
        ];
        OcrTextRegion[] regions =
        [
            Region("$ STORE", new PixelRect(10, 10, 80, 20)),
            Region("[Pl ay]", new PixelRect(10, 40, 80, 20)),
        ];

        OcrStateEvaluation evaluation = OcrRuleEngine.Evaluate("Lobby", 2, targets, regions);

        Assert.True(evaluation.IsMatch);
        Assert.Equal(["Store", "Play"], evaluation.Matches.Select(match => match.Target));
    }

    [Fact]
    public void Evaluate_DoesNotCountRepeatedTextAsDistinctTargets()
    {
        OcrTargetRule[] targets =
        [
            new("Store", "store"),
            new("Play", "play"),
        ];
        OcrTextRegion[] regions =
        [
            Region("Store", new PixelRect(10, 10, 80, 20)),
            Region("$ STORE", new PixelRect(10, 40, 80, 20)),
        ];

        OcrStateEvaluation evaluation = OcrRuleEngine.Evaluate("Lobby", 2, targets, regions);

        Assert.False(evaluation.IsMatch);
        Assert.Single(evaluation.Matches);
    }

    [Fact]
    public void FindTarget_PrefersFirstAliasAndExactTextBox()
    {
        OcrTargetRule story = new("Story", "story", "progressive gamemode", "progressive");
        OcrTextRegion[] regions =
        [
            Region("Progressive", new PixelRect(10, 10, 100, 20), 0.99),
            Region("Open Story Now", new PixelRect(10, 40, 120, 20), 0.99),
            Region("Story", new PixelRect(10, 70, 60, 20), 0.90),
        ];

        OcrTargetMatch? match = OcrRuleEngine.FindTarget(story, regions);

        Assert.NotNull(match);
        Assert.Equal("story", match.Alias);
        Assert.Equal(new PixelRect(10, 70, 60, 20), match.Region.Bounds);
    }

    [Fact]
    public void FindTarget_AllowsSymbolsAndSurroundingText()
    {
        OcrTargetRule challenge = new("Challenge", "reward gamemode");

        OcrTargetMatch? match = OcrRuleEngine.FindTarget(
            challenge,
            [Region("abc $ Rew ard Game Mode = 42", new PixelRect(1, 2, 3, 4))]);

        Assert.NotNull(match);
        Assert.Equal("abcrewardgamemode42", match.NormalizedText);
    }

    [Fact]
    public void FindTarget_ComposesAdjacentSameLineFragments()
    {
        OcrTargetMatch? match = OcrRuleEngine.FindTarget(
            new OcrTargetRule("Unit Teams", "unit teams"),
            [
                Region("Unit", new PixelRect(384, 179, 39, 19)),
                Region("Teams", new PixelRect(427, 179, 44, 19)),
            ]);

        Assert.NotNull(match);
        Assert.Equal("unitteams", match.NormalizedText);
        Assert.Equal(new PixelRect(384, 179, 87, 19), match.Region.Bounds);
    }

    [Fact]
    public void FindTarget_ComposesOutlinedFragmentsWithOverlappingBounds()
    {
        OcrTargetMatch? match = OcrRuleEngine.FindTarget(
            new OcrTargetRule("Unit Teams", "unit teams"),
            [
                Region("Unit", new PixelRect(383, 178, 43, 18)),
                Region("Teams", new PixelRect(417, 179, 54, 18)),
            ]);

        Assert.NotNull(match);
        Assert.Equal("unitteams", match.NormalizedText);
        Assert.Equal(new PixelRect(383, 178, 88, 19), match.Region.Bounds);
    }

    [Theory]
    [InlineData(480, 179)]
    [InlineData(427, 210)]
    [InlineData(400, 179)]
    public void FindTarget_DoesNotComposeSpatiallyUnrelatedFragments(int x, int y)
    {
        OcrTargetMatch? match = OcrRuleEngine.FindTarget(
            new OcrTargetRule("Unit Teams", "unit teams"),
            [
                Region("Unit", new PixelRect(384, 179, 39, 19)),
                Region("Teams", new PixelRect(x, y, 44, 19)),
            ]);

        Assert.Null(match);
    }

    [Theory]
    [InlineData("Enter Matchmaking")]
    [InlineData("Enter")]
    [InlineData("Match making")]
    public void FindTarget_AcceptsMatchmakingAnchorVariants(string text)
    {
        OcrTargetRule matchmaking = new(
            "Enter Matchmaking",
            "enter matchmaking",
            "matchmaking",
            "enter");

        OcrTargetMatch? match = OcrRuleEngine.FindTarget(
            matchmaking,
            [Region(text, new PixelRect(10, 20, 100, 20))]);

        Assert.NotNull(match);
    }

    [Theory]
    [InlineData("Spirit City")]
    [InlineData("Spirit")]
    [InlineData("City")]
    [InlineData("SP IR IT CI TY")]
    public void FindTarget_AcceptsRaidMapVariants(string text)
    {
        OcrTargetRule spiritCity = new(
            "Spirit City",
            "spirit city",
            "spirit",
            "city");

        OcrTargetMatch? match = OcrRuleEngine.FindTarget(
            spiritCity,
            [Region(text, new PixelRect(201, 328, 123, 40))]);

        Assert.NotNull(match);
        Assert.Equal(new PixelPoint(262, 328), match.Region.Bounds.TopCenter);
    }

    [Fact]
    public void Evaluate_RaidMapRejectsProgressTextWithoutMap()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.Evaluate(
            "Raid Map",
            1,
            [new OcrTargetRule("Spirit City", "spirit city", "spirit", "city")],
            [Region("Completion Progress", new PixelRect(101, 372, 152, 19))]);

        Assert.False(evaluation.IsMatch);
    }

    [Fact]
    public void Evaluate_RaidActRequiresAllThreeAnchors()
    {
        OcrTargetRule[] targets =
        [
            new("Raid", "raid"),
            new("Select Stage", "select stage"),
            new("Enter Matchmaking", "enter matchmaking", "matchmaking", "enter"),
        ];
        OcrStateEvaluation evaluation = OcrRuleEngine.Evaluate(
            "Raid Act",
            3,
            targets,
            [
                Region("Raid", new PixelRect(225, 77, 52, 26)),
                Region("Select Stage", new PixelRect(388, 574, 103, 28)),
                Region("Enter Matchmaking", new PixelRect(644, 575, 154, 25)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal(["Raid", "Select Stage", "Enter Matchmaking"],
            evaluation.Matches.Select(match => match.Target));
    }

    [Fact]
    public void FindTarget_ConfirmsRaidActFromMapTitle()
    {
        OcrTargetMatch? match = OcrRuleEngine.FindTarget(
            new OcrTargetRule("Act 2", "act 2"),
            [Region("Spirit City-Act2", new PixelRect(380, 163, 172, 27))]);

        Assert.NotNull(match);
    }

    [Fact]
    public void Evaluate_ChallengeTypeFindsHeaderDailyAndWeekly()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.Evaluate(
            "Challenge Type",
            3,
            [
                new OcrTargetRule("Challenge", "challenges", "challenge"),
                new OcrTargetRule("Daily Challenge", "daily challenge"),
                new OcrTargetRule("Weekly Challenge", "weekly challenge"),
            ],
            [
                Region("Challenges", new PixelRect(150, 50, 131, 34)),
                Region("Daily Challenge", new PixelRect(171, 273, 156, 28)),
                Region("Weekly Challenge", new PixelRect(169, 389, 178, 30)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal("Challenges", evaluation.Matches[0].Region.Text);
    }

    [Theory]
    [InlineData("Change Map")]
    [InlineData("MAP")]
    [InlineData("Disband")]
    [InlineData("Invite")]
    [InlineData("Players")]
    [InlineData("Leave Party")]
    [InlineData("Party")]
    public void Evaluate_MatchPreviewAcceptsStartAndAnySupportGroup(string support)
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Match Preview",
            2,
            MatchPreviewTargets(),
            [
                Region("Start", new PixelRect(10, 10, 60, 20)),
                Region(support, new PixelRect(100, 10, 120, 20)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal("Start", evaluation.Matches[0].Target);
    }

    [Fact]
    public void Evaluate_MatchPreviewRejectsStartWithoutSupport()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Match Preview",
            2,
            MatchPreviewTargets(),
            [Region("Start", new PixelRect(10, 10, 60, 20))]);

        Assert.False(evaluation.IsMatch);
    }

    [Fact]
    public void Evaluate_MatchPreviewRejectsSupportWithoutStart()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Match Preview",
            2,
            MatchPreviewTargets(),
            [Region("Invite Players", new PixelRect(10, 10, 120, 20))]);

        Assert.False(evaluation.IsMatch);
    }

    [Fact]
    public void Evaluate_MatchPreviewRejectsTwoSupportsWithoutStart()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Match Preview",
            2,
            MatchPreviewTargets(),
            [
                Region("Invite Players", new PixelRect(10, 10, 120, 20)),
                Region("Leave Party", new PixelRect(10, 40, 120, 20)),
            ]);

        Assert.False(evaluation.IsMatch);
        Assert.Equal(2, evaluation.Matches.Count);
    }

    [Fact]
    public void EvaluateRepeatedTarget_RequiresSeparateMatchingBoxes()
    {
        PixelRect top = new(634, 83, 96, 17);
        PixelRect bottom = new(641, 213, 84, 20);
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateRepeatedTarget(
            "Match Prestart",
            2,
            new OcrTargetRule("Start Game", "start game"),
            [
                Region("Start Game?", top),
                Region("Requires 50% to start the game.", new PixelRect(578, 118, 209, 18)),
                Region("StartGame", bottom),
                Region("START GAME", bottom, 0.80),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal([top, bottom], evaluation.Matches.Select(match => match.Region.Bounds));
    }

    [Fact]
    public void EvaluateRepeatedTarget_RejectsOneMatchingBox()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateRepeatedTarget(
            "Match Prestart",
            2,
            new OcrTargetRule("Start Game", "start game"),
            [Region("Start Game", new PixelRect(10, 20, 100, 20))]);

        Assert.False(evaluation.IsMatch);
        Assert.Single(evaluation.Matches);
    }

    [Fact]
    public void Evaluate_DefeatRequiresAnchorAndTwoSupportGroups()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Defeat",
            3,
            ResultTargets("Defeat"),
            [
                Region("Defeat", new PixelRect(168, 55, 83, 24)),
                Region("Repeat Stage", new PixelRect(272, 597, 114, 26)),
                Region("ViewParty", new PixelRect(594, 597, 96, 25)),
                Region("GameStats", new PixelRect(212, 268, 91, 17)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal("Defeat", evaluation.Matches[0].Target);
        Assert.Contains(evaluation.Matches, match => match.Target == "Repeat Stage");
    }

    [Fact]
    public void Evaluate_DefeatRejectsSupportsWithoutDefeat()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Defeat",
            3,
            ResultTargets("Defeat"),
            [
                Region("Game Stats", new PixelRect(10, 10, 100, 20)),
                Region("Clear Time", new PixelRect(10, 40, 100, 20)),
                Region("Total Damage", new PixelRect(10, 70, 100, 20)),
            ]);

        Assert.False(evaluation.IsMatch);
        Assert.Equal(3, evaluation.Matches.Count);
        Assert.False(evaluation.RequiredEvidenceMatched);
    }

    [Fact]
    public void Evaluate_DefeatRejectsOnlyOneSupportGroup()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Defeat",
            3,
            ResultTargets("Defeat"),
            [
                Region("Defeat", new PixelRect(10, 10, 100, 20)),
                Region("Repeat", new PixelRect(10, 40, 100, 20)),
            ]);

        Assert.False(evaluation.IsMatch);
        Assert.True(evaluation.RequiredEvidenceMatched);
    }

    [Fact]
    public void Evaluate_VictoryRequiresAnchorAndTwoSupportGroups()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Victory",
            3,
            ResultTargets("Victory"),
            [
                Region("Victory", new PixelRect(167, 56, 92, 31)),
                Region("Repeat Stage", new PixelRect(431, 599, 112, 25)),
                Region("ViewParty", new PixelRect(646, 597, 97, 26)),
                Region("GameStats", new PixelRect(211, 268, 92, 17)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal("Victory", evaluation.Matches[0].Target);
        Assert.Contains(evaluation.Matches, match => match.Target == "Repeat Stage");
    }

    [Fact]
    public void Evaluate_VictoryRejectsSupportsWithoutVictory()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Victory",
            3,
            ResultTargets("Victory"),
            [
                Region("Repeat Stage", new PixelRect(10, 10, 100, 20)),
                Region("View Party", new PixelRect(10, 40, 100, 20)),
                Region("Total Yen", new PixelRect(10, 70, 100, 20)),
            ]);

        Assert.False(evaluation.IsMatch);
        Assert.False(evaluation.RequiredEvidenceMatched);
    }

    [Fact]
    public void Evaluate_ExpeditionMapRequiresAllThreeMapGroups()
    {
        OcrTargetRule[] targets =
        [
            new("School Grounds", "school", "grounds", "school grounds"),
            new("Flower Forest", "flower", "forest", "flower forest"),
            new("Rose Kingdom", "rose", "kingdom", "rose kingdom"),
        ];
        OcrTextRegion[] regions =
        [
            Region("School Grounds", new PixelRect(132, 222, 157, 24)),
            Region("Flower Forest", new PixelRect(150, 366, 140, 26)),
            Region("Rose Kingdom", new PixelRect(148, 514, 141, 27)),
        ];

        Assert.True(OcrRuleEngine.Evaluate("Expedition Map", 3, targets, regions).IsMatch);
        Assert.False(OcrRuleEngine.Evaluate("Expedition Map", 3, targets, regions[..2]).IsMatch);
    }

    [Fact]
    public void FindLeftmostTarget_PrefersMapListTextOverDetailHeading()
    {
        OcrTargetRule target = new("School Grounds", "school", "grounds", "school grounds");
        OcrTextRegion leftList = Region(
            "School Grounds",
            new PixelRect(132, 222, 157, 24),
            0.98);
        OcrTextRegion detail = Region(
            "School Grounds",
            new PixelRect(347, 91, 258, 37),
            1.0);

        OcrTargetMatch? match = OcrRuleEngine.FindLeftmostTarget(target, [detail, leftList]);

        Assert.NotNull(match);
        Assert.Equal(leftList.Bounds, match.Region.Bounds);
    }

    [Theory]
    [InlineData("Unequip")]
    [InlineData("Unequip All")]
    [InlineData("Quick")]
    [InlineData("Quick Sell")]
    public void Evaluate_UnitInventoryAcceptsAnyConfiguredAction(string action)
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Unit Inventory",
            2,
            UnitInventoryTargets(),
            [
                Region("Teams", new PixelRect(460, 595, 56, 22)),
                Region(action, new PixelRect(234, 596, 95, 20)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.True(evaluation.RequiredEvidenceMatched);
        Assert.Equal("Teams", evaluation.Matches[0].Target);
    }

    [Fact]
    public void Evaluate_UnitInventoryRejectsActionWithoutTeams()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Unit Inventory",
            2,
            UnitInventoryTargets(),
            [Region("Quick Sell", new PixelRect(654, 595, 82, 20))]);

        Assert.False(evaluation.IsMatch);
        Assert.False(evaluation.RequiredEvidenceMatched);
    }

    private static OcrTargetRule[] MatchPreviewTargets() =>
    [
        new("Start", "start"),
        new("Change Map", "change map", "change", "map"),
        new("Disband", "disband"),
        new("Invite Players", "invite players", "invite", "players"),
        new("Leave Party", "leave party", "leave", "party"),
    ];

    private static OcrTargetRule[] ResultTargets(string result) =>
    [
        new(result, result.ToLowerInvariant()),
        new("Repeat Stage", "repeat stage", "repeat"),
        new("View Party", "view party", "party"),
        new("Game Stats", "game stats"),
        new("Gained Rewards", "gained rewards"),
        new("Clear Time", "clear time"),
        new("Total Yen", "total yen"),
        new("Total Kills", "total kills"),
        new("Total Damage", "total damage"),
    ];

    private static OcrTargetRule[] UnitInventoryTargets() =>
    [
        new("Teams", "teams"),
        new("Inventory Action", "unequip all", "unequip", "quick sell", "quick"),
    ];

    private static OcrTextRegion Region(string text, PixelRect bounds, double confidence = 0.95) => new()
    {
        Bounds = bounds,
        Text = text,
        RecognitionConfidence = confidence,
    };
}
