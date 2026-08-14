using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class CalendarClaimPolicyTests
{
    [Fact]
    public void ResolvesSevenCardsInReverseDayOrder()
    {
        OcrTextRegion[] regions =
        [
            Day(1, 300, 220), Day(2, 502, 218), Day(3, 700, 223), Day(7, 901, 219),
            Day(4, 303, 410), Day(5, 499, 412), Day(6, 702, 408),
        ];
        Assert.True(CalendarClaimPolicy.TryResolveClaimPoints(
            regions, new PixelSize(1366, 700), out IReadOnlyList<PixelPoint> points));
        Assert.Equal(7, points.Count);
        Assert.Equal(new PixelPoint(987, 305), points[0]);
        Assert.Equal(new PixelPoint(386, 306), points[^1]);
    }

    [Fact]
    public void RejectsIncompleteOrImplausibleCalendarGeometry()
    {
        Assert.False(CalendarClaimPolicy.TryResolveClaimPoints(
            [Day(1, 300, 220)], new PixelSize(1366, 700), out _));
        OcrTextRegion[] compressed = Enumerable.Range(1, 7)
            .Select(day => Day(day, 300 + day * 20, day < 4 ? 220 : 410))
            .ToArray();
        Assert.False(CalendarClaimPolicy.TryResolveClaimPoints(
            compressed, new PixelSize(1366, 700), out _));
    }

    private static OcrTextRegion Day(int day, int x, int y) => new()
    {
        Bounds = new PixelRect(x, y, 40, 20),
        Text = $"Day {day}",
        RecognitionConfidence = 0.99,
    };
}
