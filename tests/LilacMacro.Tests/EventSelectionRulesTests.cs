using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class EventSelectionRulesTests
{
    [Fact]
    public void State_RequiresEventsBackAndCalendar()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateExact(
            "Event Select",
            3,
            EventSelectionRules.StateTargets,
            [
                Region("Events", new PixelRect(43, 91, 81, 24)),
                Region("Release Calendar", new PixelRect(45, 486, 168, 20)),
                Region("Back", new PixelRect(91, 642, 47, 22)),
                Region("Calendar", new PixelRect(244, 643, 78, 20)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal(3, evaluation.Matches.Count);
        Assert.Equal(
            new PixelRect(244, 643, 78, 20),
            evaluation.Matches.Single(match => match.Target == "Calendar").Region.Bounds);
    }

    [Theory]
    [InlineData("Events", "Calendar")]
    [InlineData("Events", "Back")]
    [InlineData("Back", "Calendar")]
    public void State_RejectsAnyMissingAnchor(string first, string second)
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateExact(
            "Event Select",
            3,
            EventSelectionRules.StateTargets,
            [
                Region(first, new PixelRect(10, 10, 100, 20)),
                Region(second, new PixelRect(10, 40, 100, 20)),
                Region("Villain Invasion", new PixelRect(70, 183, 149, 24)),
            ]);

        Assert.False(evaluation.IsMatch);
    }

    [Fact]
    public void State_RejectsReleaseCalendarWithoutCalendarButton()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateExact(
            "Event Select",
            3,
            EventSelectionRules.StateTargets,
            [
                Region("Events", new PixelRect(43, 91, 81, 24)),
                Region("Back", new PixelRect(91, 642, 47, 22)),
                Region("Release Calendar", new PixelRect(45, 486, 168, 20)),
            ]);

        Assert.False(evaluation.IsMatch);
        Assert.DoesNotContain(evaluation.Matches, match => match.Target == "Calendar");
    }

    [Theory]
    [InlineData("Villain Invasion")]
    [InlineData("Invasion")]
    public void Find_AcceptsVillainInvasionAliases(string text)
    {
        PixelRect bounds = new(70, 183, 149, 24);

        OcrTargetMatch? match = EventSelectionRules.Find(
            EventDestination.VillainInvasion,
            [Region(text, bounds)]);

        Assert.NotNull(match);
        Assert.Equal("Villain Invasion", match.Target);
        Assert.Equal(bounds.Center, match.Region.Bounds.Center);
    }

    [Theory]
    [InlineData(EventDestination.BossBounty, "Boss Bounty", "Boss Bounty")]
    [InlineData(EventDestination.GuessThatUnit, "Guess That Unit", "Guess That Unit")]
    public void Find_AcceptsUpdatedEventTargets(
        EventDestination destination,
        string text,
        string expectedTarget)
    {
        PixelRect bounds = new(70, 183, 149, 24);

        OcrTargetMatch? match = EventSelectionRules.Find(
            destination,
            [Region(text, bounds)]);

        Assert.NotNull(match);
        Assert.Equal(expectedTarget, match.Target);
        Assert.Equal(bounds.Center, match.Region.Bounds.Center);
    }

    private static OcrTextRegion Region(string text, PixelRect bounds) => new()
    {
        Bounds = bounds,
        Text = text,
        RecognitionConfidence = 0.99,
    };
}
