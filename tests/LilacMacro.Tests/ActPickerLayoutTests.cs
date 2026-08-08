using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class ActPickerLayoutTests
{
    private static readonly PixelSize ClientSize = new(1366, 700);

    [Fact]
    public void Story_UsesTopmostModeInsteadOfHigherConfidenceInnerTitle()
    {
        OcrTextRegion topStory = Region("Story", new PixelRect(223, 75, 66, 34), 0.98);
        OcrTextRegion innerStory = Region("Story", new PixelRect(383, 142, 42, 23), 1.0);

        ActPickerLayout? layout = ActPickerLayout.TryCreate(
            [innerStory, Region("Select Stage", new PixelRect(388, 574, 102, 27)), topStory],
            ClientSize,
            ActPickerKind.Story);

        Assert.NotNull(layout);
        Assert.Equal(topStory.Bounds, layout.ModeBounds);
    }

    [Fact]
    public void StoryLargeScale_ProducesSafeActAndDifficultyPoints()
    {
        ActPickerLayout layout = CreateStory(
            new PixelRect(222, 76, 67, 32),
            new PixelRect(387, 574, 104, 28));

        Assert.Equal(new PixelPoint(255, 165), layout.GetActPoint(StoryAct.Act1));
        Assert.Equal(new PixelPoint(255, 377), layout.GetActPoint(StoryAct.Act4));
        Assert.Equal(new PixelPoint(255, 588), layout.GetActPoint(StoryAct.Mastery));
        Assert.Equal(new PixelPoint(349, 305), layout.GetDifficultyPoint(StoryDifficulty.Normal));
        Assert.Equal(new PixelPoint(417, 305), layout.GetDifficultyPoint(StoryDifficulty.Hard));
        Assert.Equal(new PixelPoint(439, 588), layout.SelectStagePoint);
    }

    [Fact]
    public void StorySmallScale_AcceptsSpaceFreeSelectStage()
    {
        ActPickerLayout? layout = ActPickerLayout.TryCreate(
            [
                Region("Story", new PixelRect(348, 150, 50, 23)),
                Region("SelectStage", new PixelRect(468, 515, 78, 17)),
            ],
            ClientSize,
            ActPickerKind.Story);

        Assert.NotNull(layout);
        Assert.Equal(new PixelPoint(373, 215), layout.GetActPoint(StoryAct.Act1));
        Assert.Equal(new PixelPoint(373, 472), layout.GetActPoint(StoryAct.Infinite));
        Assert.Equal(new PixelPoint(373, 523), layout.GetActPoint(StoryAct.Mastery));
        Assert.Equal(new PixelPoint(442, 317), layout.GetDifficultyPoint(StoryDifficulty.Normal));
        Assert.Equal(new PixelPoint(491, 317), layout.GetDifficultyPoint(StoryDifficulty.Hard));
    }

    [Fact]
    public void Raid_UsesTopmostModeAndThreeScaleRelativeRows()
    {
        OcrTextRegion topRaid = Region("Raid", new PixelRect(225, 77, 52, 26), 1.0);
        OcrTextRegion innerRaid = Region("Raid", new PixelRect(381, 145, 36, 18), 0.99);
        ActPickerLayout? layout = ActPickerLayout.TryCreate(
            [innerRaid, Region("Select Stage", new PixelRect(388, 574, 103, 28)), topRaid],
            ClientSize,
            ActPickerKind.Raid);

        Assert.NotNull(layout);
        Assert.Equal(topRaid.Bounds, layout.ModeBounds);
        Assert.Equal(new PixelPoint(251, 211), layout.GetActPoint(StoryAct.Act1));
        Assert.Equal(new PixelPoint(251, 374), layout.GetActPoint(StoryAct.Act2));
        Assert.Equal(new PixelPoint(251, 539), layout.GetActPoint(StoryAct.Act3));
        Assert.False(layout.SupportsDifficulty);
        Assert.False(layout.SupportsAct(StoryAct.Act4));
    }

    [Fact]
    public void TryCreate_RejectsMissingOrReversedAnchors()
    {
        Assert.Null(ActPickerLayout.TryCreate(
            [Region("Story", new PixelRect(100, 100, 50, 20))],
            ClientSize,
            ActPickerKind.Story));
        Assert.Null(ActPickerLayout.TryCreate(
            [
                Region("Raid", new PixelRect(500, 500, 50, 20)),
                Region("Select Stage", new PixelRect(400, 100, 80, 20)),
            ],
            ClientSize,
            ActPickerKind.Raid));
    }

    private static ActPickerLayout CreateStory(PixelRect story, PixelRect selectStage) =>
        ActPickerLayout.TryCreate(
            [Region("Story", story), Region("Select Stage", selectStage)],
            ClientSize,
            ActPickerKind.Story) ?? throw new InvalidOperationException("Expected valid test layout.");

    private static OcrTextRegion Region(string text, PixelRect bounds, double confidence = 0.95) => new()
    {
        Bounds = bounds,
        Text = text,
        RecognitionConfidence = confidence,
    };
}
