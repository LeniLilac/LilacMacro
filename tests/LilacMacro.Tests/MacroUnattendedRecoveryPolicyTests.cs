using LilacMacro.App.Runtime;
using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class MacroUnattendedRecoveryPolicyTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 30)]
    [InlineData(200, 30)]
    public void RetryDelayExpandsAndRemainsBounded(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), MacroUnattendedRecoveryPolicy.RetryDelay(failures));

    [Fact]
    public void RetryDelayRejectsMissingFailure() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MacroUnattendedRecoveryPolicy.RetryDelay(0));

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(20, true)]
    public void TaskQuarantineBeginsAfterThreeFailures(int failures, bool expected) =>
        Assert.Equal(expected, MacroUnattendedRecoveryPolicy.ShouldQuarantineTask(failures));

    [Fact]
    public void TaskQuarantineIsTemporary() =>
        Assert.Equal(TimeSpan.FromMinutes(5), MacroUnattendedRecoveryPolicy.TaskQuarantineDuration);

    [Fact]
    public async Task PreflightRejectsEmptyPlanBeforeTaskValidation()
    {
        PlanPrototype plan = new("empty", []);
        int calls = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => MacroPlanPreflight.ValidateAsync(
            plan,
            (_, _) => { calls++; return Task.CompletedTask; },
            CancellationToken.None));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task PreflightRejectsUnsupportedTaskBeforeTaskValidation()
    {
        PlanTaskPrototype unsupported = new() { Mode = PlanTaskMode.Event };
        PlanPrototype plan = new("unsupported", [unsupported]);
        int calls = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => MacroPlanPreflight.ValidateAsync(
            plan,
            (_, _) => { calls++; return Task.CompletedTask; },
            CancellationToken.None));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task PreflightValidatesEverySupportedTask()
    {
        PlanTaskPrototype story = new() { Mode = PlanTaskMode.Story };
        PlanTaskPrototype raid = new() { Mode = PlanTaskMode.Raid };
        PlanPrototype plan = new("supported", [story, raid]);
        List<PlanTaskPrototype> validated = [];

        await MacroPlanPreflight.ValidateAsync(
            plan,
            (task, _) => { validated.Add(task); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal([story, raid], validated);
    }
}
