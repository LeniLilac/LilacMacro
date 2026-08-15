using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementMarkerLabelLayoutTests
{
    [Fact]
    public void DisplayLabels_AddLettersOnlyWhenAUnitSlotRepeats()
    {
        PlacementStep one = Placement(1, 100, 100);
        PlacementStep sixA = Placement(6, 200, 200);
        PlacementStep sixB = Placement(6, 300, 300);
        PlacementStep sixC = Placement(6, 400, 400);

        IReadOnlyDictionary<Guid, string> labels =
            PlacementReferencePolicy.BuildDisplayLabels([one, sixA, sixB, sixC]);

        Assert.Equal("1", labels[one.Id]);
        Assert.Equal("6a", labels[sixA.Id]);
        Assert.Equal("6b", labels[sixB.Id]);
        Assert.Equal("6c", labels[sixC.Id]);
    }

    [Fact]
    public void DenseCluster_FansLabelsIntoSeparatedLanes()
    {
        PlacementMarkerLabelRequest[] requests =
        [
            Request(398, 350),
            Request(410, 362),
            Request(422, 374),
            Request(398, 386),
            Request(410, 398),
            Request(422, 410),
        ];

        IReadOnlyList<PlacementMarkerLabelPlacement> placements =
            PlacementMarkerLabelLayout.Arrange(requests, 1366, 700);

        Assert.Equal(requests.Length, placements.Count);
        Assert.All(placements, placement => Assert.True(placement.LabelBounds.IsInside(new PixelSize(1366, 700))));
        for (int first = 0; first < placements.Count; first++)
        {
            for (int second = first + 1; second < placements.Count; second++)
            {
                Assert.False(Intersects(Expand(placements[first].LabelBounds, 4), placements[second].LabelBounds));
            }
        }
    }

    private static PlacementStep Placement(int unitSlot, int x, int y) =>
        PlacementStep.CreatePlace(
            unitSlot,
            x,
            y,
            PlacementTargetingPriority.First,
            PlacementAutoUpgradePriority.Priority1);

    private static PlacementMarkerLabelRequest Request(int x, int y) =>
        new(Guid.NewGuid(), x, y, 48, 24);

    private static PixelRect Expand(PixelRect region, int amount) => new(
        region.X - amount,
        region.Y - amount,
        region.Width + amount * 2,
        region.Height + amount * 2);

    private static bool Intersects(PixelRect first, PixelRect second) =>
        first.X < second.Right &&
        first.Right > second.X &&
        first.Y < second.Bottom &&
        first.Bottom > second.Y;
}
