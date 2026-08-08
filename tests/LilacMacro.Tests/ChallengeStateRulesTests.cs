using LilacMacro.App.Debugging;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class ChallengeStateRulesTests
{
    [Fact]
    public void AvailableRequiresHeaderMapBackAndSelectStage()
    {
        OcrStateEvaluation result = DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ChallengeAvailable,
            Regions("Challenges", "Flower Forest - Act 2", "Back", "Select Stage"));

        Assert.True(result.IsMatch);
        Assert.Contains(result.Matches, match => match.Target == "Flower Forest");
    }

    [Fact]
    public void CooldownAcceptsDynamicFuzzyAvailableInPhrase()
    {
        OcrStateEvaluation result = DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ChallengeCooldown,
            Regions(
                ("Challenges", 40),
                ("Fairy King Forest - Act 5", 160),
                ("Back", 598),
                ("Availab1e 1n 06:44:41", 599),
                ("Enter Matchmaking", 595)));

        Assert.True(result.IsMatch);
        Assert.Contains(result.Matches, match => match.Target == "Available In");
    }

    [Fact]
    public void CooldownRejectsRewardsAvailableInsteadOfAvailableIn()
    {
        OcrStateEvaluation result = DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ChallengeCooldown,
            Regions(
                ("Challenges", 40),
                ("King's Tomb - Act 5", 160),
                ("Back", 598),
                ("Rewards Available", 599),
                ("Enter Matchmaking", 595)));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void MissingTopLevelChallengesEvidenceFailsClosed()
    {
        OcrStateEvaluation result = DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ChallengeAvailable,
            Regions("Daily Challenge", "School Grounds - Act 1", "Back", "Select Stage"));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void MultipleMapNamesCannotReplaceBackOrSelectStageEvidence()
    {
        OcrStateEvaluation result = DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ChallengeAvailable,
            Regions("Challenges", "School Grounds", "Rose Kingdom", "Fairy King Forest"));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void CooldownRejectsAvailableInOutsideBottomActionRow()
    {
        OcrStateEvaluation result = DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.ChallengeCooldown,
            Regions(
                ("Challenges", 40),
                ("School Grounds - Act 1", 160),
                ("Availab1e 1n 06:44:41", 280),
                ("Back", 598),
                ("Enter Matchmaking", 595)));

        Assert.False(result.IsMatch);
    }

    private static OcrTextRegion[] Regions(params string[] text) => text
        .Select((value, index) => new OcrTextRegion
        {
            Bounds = new PixelRect(20, 20 + index * 30, 200, 20),
            Text = value,
            RecognitionConfidence = 0.99,
        })
        .ToArray();

    private static OcrTextRegion[] Regions(params (string Text, int Y)[] values) => values
        .Select(value => new OcrTextRegion
        {
            Bounds = new PixelRect(20, value.Y, 200, 20),
            Text = value.Text,
            RecognitionConfidence = 0.99,
        })
        .ToArray();
}
