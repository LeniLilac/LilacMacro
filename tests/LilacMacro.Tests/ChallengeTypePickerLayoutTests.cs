using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class ChallengeTypePickerLayoutTests
{
    private static readonly PixelSize ClientSize = new(1366, 700);

    [Fact]
    public void LargeScale_DerivesThreeRegularChallengeRows()
    {
        ChallengeTypePickerLayout? layout = ChallengeTypePickerLayout.TryCreate(
            [
                Region("Challenges", new PixelRect(150, 50, 131, 34)),
                Region("Daily Challenge", new PixelRect(171, 273, 156, 28)),
                Region("Weekly Challenge", new PixelRect(169, 389, 178, 30)),
            ],
            ClientSize);

        Assert.NotNull(layout);
        Assert.Equal(new PixelPoint(1175, 230), layout.GetTypePoint(RegularChallengeType.Trait));
        Assert.Equal(new PixelPoint(1175, 407), layout.GetTypePoint(RegularChallengeType.Stat));
        Assert.Equal(new PixelPoint(1175, 584), layout.GetTypePoint(RegularChallengeType.Sprite));
    }

    [Fact]
    public void SmallScale_PreservesDerivedTargets()
    {
        ChallengeTypePickerLayout? layout = ChallengeTypePickerLayout.TryCreate(
            [
                Region("Challenges", new PixelRect(328, 149, 88, 23)),
                Region("DailyChallenge", new PixelRect(342, 299, 97, 16)),
                Region("Weekly Challenge", new PixelRect(341, 375, 113, 20)),
            ],
            ClientSize);

        Assert.NotNull(layout);
        Assert.Equal(new PixelPoint(1013, 269), layout.GetTypePoint(RegularChallengeType.Trait));
        Assert.Equal(new PixelPoint(1013, 387), layout.GetTypePoint(RegularChallengeType.Stat));
        Assert.Equal(new PixelPoint(1013, 505), layout.GetTypePoint(RegularChallengeType.Sprite));
    }

    [Fact]
    public void TryCreate_RejectsMissingOrReversedAnchors()
    {
        Assert.Null(ChallengeTypePickerLayout.TryCreate(
            [Region("Challenges", new PixelRect(100, 100, 100, 20))],
            ClientSize));
        Assert.Null(ChallengeTypePickerLayout.TryCreate(
            [
                Region("Challenges", new PixelRect(100, 300, 100, 20)),
                Region("Daily Challenge", new PixelRect(100, 200, 100, 20)),
                Region("Weekly Challenge", new PixelRect(100, 100, 100, 20)),
            ],
            ClientSize));
    }

    private static OcrTextRegion Region(string text, PixelRect bounds) => new()
    {
        Bounds = bounds,
        Text = text,
        RecognitionConfidence = 0.99,
    };
}
