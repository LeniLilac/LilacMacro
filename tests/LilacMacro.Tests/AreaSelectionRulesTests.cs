using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class AreaSelectionRulesTests
{
    [Fact]
    public void State_RequiresAreasAndTwoSupportingCategories()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Areas UI",
            3,
            AreaSelectionRules.StateTargets,
            [
                Region("Areas", new PixelRect(175, 54, 75, 28)),
                Region("Upgrade", new PixelRect(250, 123, 81, 28)),
                Region("Gamemode", new PixelRect(242, 178, 97, 24)),
            ]);

        Assert.True(evaluation.IsMatch);
        Assert.Equal(["Areas", "Upgrade", "Gamemode"], evaluation.Matches.Select(match => match.Target));
    }

    [Fact]
    public void State_RejectsSupportingCategoriesWithoutAreas()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Areas UI",
            3,
            AreaSelectionRules.StateTargets,
            [
                Region("Upgrade", new PixelRect(250, 123, 81, 28)),
                Region("Gamemode", new PixelRect(242, 178, 97, 24)),
                Region("Lobby", new PixelRect(258, 229, 62, 29)),
            ]);

        Assert.False(evaluation.IsMatch);
        Assert.False(evaluation.RequiredEvidenceMatched);
    }

    [Fact]
    public void State_RejectsAreasWithOnlyOneSupportingCategory()
    {
        OcrStateEvaluation evaluation = OcrRuleEngine.EvaluateWithRequiredFirstTarget(
            "Areas UI",
            3,
            AreaSelectionRules.StateTargets,
            [
                Region("Areas", new PixelRect(175, 54, 75, 28)),
                Region("Shop", new PixelRect(264, 283, 51, 28)),
            ]);

        Assert.False(evaluation.IsMatch);
    }

    [Fact]
    public void Find_PrefersLeftNavigationCategoryOverContentHeading()
    {
        PixelRect navigation = new(250, 123, 81, 28);
        OcrTargetMatch? match = AreaSelectionRules.Find(
            AreaCategory.Upgrade,
            [
                Region("Areas", new PixelRect(175, 54, 75, 28)),
                Region("Upgrade Areas", new PixelRect(387, 116, 150, 28)),
                Region("Upgrades", new PixelRect(398, 520, 100, 32)),
                Region("Upgrade", navigation),
            ]);

        Assert.NotNull(match);
        Assert.Equal(navigation, match.Region.Bounds);
        Assert.Equal(new PixelPoint(290, 137), match.Region.Bounds.Center);
    }

    [Fact]
    public void Find_RejectsContentHeadingWithoutLeftNavigationCategory()
    {
        OcrTargetMatch? match = AreaSelectionRules.Find(
            AreaCategory.Upgrade,
            [
                Region("Areas", new PixelRect(175, 54, 75, 28)),
                Region("Upgrade Areas", new PixelRect(387, 116, 150, 28)),
                Region("Upgrades", new PixelRect(398, 520, 100, 32)),
            ]);

        Assert.Null(match);
    }

    [Theory]
    [InlineData(AreaCategory.Upgrade, "Upgrade")]
    [InlineData(AreaCategory.Gamemode, "Gamemode")]
    [InlineData(AreaCategory.Lobby, "Lobby")]
    [InlineData(AreaCategory.Shop, "Shop")]
    [InlineData(AreaCategory.Expedition, "Expedition")]
    public void Find_RecognizesEachCategory(AreaCategory category, string text)
    {
        OcrTargetMatch? match = AreaSelectionRules.Find(
            category,
            [
                Region("Areas", new PixelRect(100, 50, 80, 30)),
                Region(text, new PixelRect(180, 100, 100, 30)),
            ]);

        Assert.NotNull(match);
        Assert.Equal(text, match.Target);
    }

    private static OcrTextRegion Region(string text, PixelRect bounds) => new()
    {
        Bounds = bounds,
        Text = text,
        RecognitionConfidence = 0.99,
    };
}
