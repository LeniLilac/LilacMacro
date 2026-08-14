using LilacMacro.App.Controls;

namespace LilacMacro.Tests;

public sealed class RunStatsChartTests
{
    [Fact]
    public void BuildCumulativeSeriesCarriesCompletedRunsAcrossEmptyBuckets()
    {
        RunStatsPoint[] runs =
        [
            new(TimeSpan.FromSeconds(10), true),
            new(TimeSpan.FromSeconds(30), false),
            new(TimeSpan.FromSeconds(90), true),
        ];

        IReadOnlyList<RunStatsCumulativePoint> series =
            RunStatsChart.BuildCumulativeSeries(runs, 10, 100);

        Assert.Equal(new RunStatsCumulativePoint(1, 0), series[1]);
        Assert.Equal(new RunStatsCumulativePoint(1, 0), series[2]);
        Assert.Equal(new RunStatsCumulativePoint(1, 1), series[3]);
        Assert.Equal(new RunStatsCumulativePoint(1, 1), series[8]);
        Assert.Equal(new RunStatsCumulativePoint(2, 1), series[9]);
    }

    [Fact]
    public void BuildCumulativeSeriesClampsNegativeAndMaximumElapsedValues()
    {
        RunStatsPoint[] runs =
        [
            new(TimeSpan.FromSeconds(-1), false),
            new(TimeSpan.FromSeconds(100), true),
        ];

        IReadOnlyList<RunStatsCumulativePoint> series =
            RunStatsChart.BuildCumulativeSeries(runs, 4, 100);

        Assert.Equal(new RunStatsCumulativePoint(0, 1), series[0]);
        Assert.Equal(new RunStatsCumulativePoint(0, 1), series[2]);
        Assert.Equal(new RunStatsCumulativePoint(1, 1), series[3]);
    }

    [Fact]
    public void BuildCumulativeSeriesRejectsInvalidChartBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunStatsChart.BuildCumulativeSeries([], 1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunStatsChart.BuildCumulativeSeries([], 10, 0));
    }
}
