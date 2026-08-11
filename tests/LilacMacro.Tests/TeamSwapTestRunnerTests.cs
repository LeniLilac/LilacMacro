using LilacMacro.App.Debugging;

namespace LilacMacro.Tests;

public sealed class TeamSwapTestRunnerTests
{
    [Fact]
    public void CreateBalancedRandomTeams_CoversEveryTeamBeforeRepeating()
    {
        IReadOnlyList<int> teams = TeamSwapTestRunner.CreateBalancedRandomTeams(16, 42);

        Assert.Equal(16, teams.Count);
        Assert.Equal(Enumerable.Range(1, 8), teams.Take(8).Order());
        Assert.Equal(Enumerable.Range(1, 8), teams.Skip(8).Order());
    }

    [Fact]
    public void CreateBalancedRandomTeams_IsDeterministicForRecordedSeed()
    {
        IReadOnlyList<int> first = TeamSwapTestRunner.CreateBalancedRandomTeams(19, 90125);
        IReadOnlyList<int> second = TeamSwapTestRunner.CreateBalancedRandomTeams(19, 90125);

        Assert.Equal(first, second);
        Assert.All(first, team => Assert.InRange(team, 1, 8));
    }

    [Fact]
    public void CreateBalancedRandomTeams_RejectsEmptyRun()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TeamSwapTestRunner.CreateBalancedRandomTeams(0, 1));
    }
}
