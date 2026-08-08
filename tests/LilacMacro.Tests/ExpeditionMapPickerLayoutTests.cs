using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class ExpeditionMapPickerLayoutTests
{
    private static readonly PixelSize ClientSize = new(1366, 700);

    [Fact]
    public void LargeScale_DerivesControlsFromTopmostDifficulty()
    {
        ExpeditionMapPickerLayout? layout = ExpeditionMapPickerLayout.TryCreate(
            [
                Region("Difficulty 1", new PixelRect(462, 380, 79, 20), 1.0),
                Region("Difficulty", new PixelRect(375, 306, 91, 26), 0.99),
                Region("Select Stage", new PixelRect(450, 643, 107, 27)),
            ],
            ClientSize);

        Assert.NotNull(layout);
        Assert.Equal(new PixelRect(375, 306, 91, 26), layout.DifficultyBounds);
        Assert.Equal(337, layout.VerticalSpan);
        Assert.Equal(new PixelPoint(391, 377), layout.MinusPoint);
        Assert.Equal(new PixelPoint(610, 377), layout.PlusPoint);
        Assert.Equal(new PixelPoint(503, 656), layout.SelectStagePoint);
    }

    [Fact]
    public void SmallScale_PreservesDerivedControlPositions()
    {
        ExpeditionMapPickerLayout? layout = ExpeditionMapPickerLayout.TryCreate(
            [
                Region("Difficulty", new PixelRect(255, 441, 56, 11)),
                Region("Difficulty1", new PixelRect(311, 489, 49, 11)),
                Region("Go SelectStage!", new PixelRect(295, 662, 79, 19)),
            ],
            ClientSize);

        Assert.NotNull(layout);
        Assert.Equal(225, layout.VerticalSpan);
        Assert.Equal(new PixelPoint(263, 485), layout.MinusPoint);
        Assert.Equal(new PixelPoint(410, 485), layout.PlusPoint);
        Assert.Equal(new PixelPoint(334, 671), layout.SelectStagePoint);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void Difficulty_MapsToIncreaseClickCount(int difficulty, int expected)
    {
        Assert.Equal(expected, ExpeditionMapPickerLayout.GetIncreaseClickCount(difficulty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Difficulty_RejectsValuesOutsideOneThroughThree(int difficulty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpeditionMapPickerLayout.GetIncreaseClickCount(difficulty));
    }

    [Fact]
    public void TryCreate_RejectsMissingOrReversedAnchors()
    {
        Assert.Null(ExpeditionMapPickerLayout.TryCreate(
            [Region("Difficulty", new PixelRect(300, 300, 80, 20))],
            ClientSize));
        Assert.Null(ExpeditionMapPickerLayout.TryCreate(
            [
                Region("Difficulty", new PixelRect(300, 500, 80, 20)),
                Region("Select Stage", new PixelRect(400, 100, 100, 20)),
            ],
            ClientSize));
    }

    [Fact]
    public void FindSelectedMap_RequiresASecondMatchAboveAndRightOfTheListLabel()
    {
        OcrTargetRule target = new("Rose Kingdom", "rose", "kingdom", "rose kingdom");
        PixelRect listBounds = new(148, 514, 141, 27);
        OcrTextRegion detail = Region("Rose Kingdom", new PixelRect(347, 91, 258, 37));
        OcrTextRegion list = Region("Rose Kingdom", listBounds);

        OcrTargetMatch? selected = ExpeditionMapPickerLayout.FindSelectedMap(
            target,
            [list, detail],
            listBounds);

        Assert.NotNull(selected);
        Assert.Equal(detail.Bounds, selected.Region.Bounds);
        Assert.Null(ExpeditionMapPickerLayout.FindSelectedMap(target, [list], listBounds));
    }

    private static OcrTextRegion Region(
        string text,
        PixelRect bounds,
        double confidence = 0.95) => new()
        {
            Bounds = bounds,
            Text = text,
            RecognitionConfidence = confidence,
        };
}
