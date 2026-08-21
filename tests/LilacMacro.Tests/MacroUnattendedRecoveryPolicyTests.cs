using LilacMacro.App.Diagnostics;
using LilacMacro.App.Runtime;
using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class MacroUnattendedRecoveryPolicyTests
{
    [Fact]
    public async Task RecoveryPreservesOwningSynchronizationContextAcrossSecondAttempt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-recovery-context-{Guid.NewGuid():N}");
        SynchronizationContext? previous = SynchronizationContext.Current;
        InlineSynchronizationContext context = new();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            PlanTaskPrototype task = new() { Mode = PlanTaskMode.Story };
            PlanPrototype plan = new("context", [task]);
            List<bool> callbackContexts = [];
            int attempts = 0;
            MacroUnattendedRecoveryRunner runner = new(
                new Dictionary<PlanTaskPrototype, DateTimeOffset>(),
                () => task,
                () => callbackContexts.Add(ReferenceEquals(context, SynchronizationContext.Current)),
                _ => callbackContexts.Add(ReferenceEquals(context, SynchronizationContext.Current)),
                _ => callbackContexts.Add(ReferenceEquals(context, SynchronizationContext.Current)),
                new DeepDebugSessionService(root),
                async _ => await Task.Run(static () => { }),
                async (_, _) =>
                {
                    callbackContexts.Add(ReferenceEquals(context, SynchronizationContext.Current));
                    await Task.Run(static () => { });
                    callbackContexts.Add(ReferenceEquals(context, SynchronizationContext.Current));
                },
                async (_, _) => await Task.Run(static () => { }));

            await runner.RunAsync(
                plan,
                (_, _) =>
                {
                    callbackContexts.Add(ReferenceEquals(context, SynchronizationContext.Current));
                    if (attempts++ == 0) throw new InvalidOperationException("retry");
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.Equal(2, attempts);
            Assert.NotEmpty(callbackContexts);
            Assert.All(callbackContexts, Assert.True);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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
    public void MatchTaskQuarantineIsTemporary() =>
        Assert.Equal(TimeSpan.FromMinutes(5), MacroUnattendedRecoveryPolicy.TaskQuarantineDuration);

    [Theory]
    [InlineData(PlanTaskMode.Utilities, 1, false)]
    [InlineData(PlanTaskMode.Utilities, 2, false)]
    [InlineData(PlanTaskMode.Utilities, 3, true)]
    [InlineData(PlanTaskMode.Story, 3, false)]
    public void UtilityOnlyUsesIndefiniteQuarantineAfterThreeFailures(
        PlanTaskMode mode,
        int failures,
        bool expected) =>
        Assert.Equal(expected, MacroUnattendedRecoveryPolicy.ShouldQuarantineIndefinitely(mode, failures));

    [Theory]
    [InlineData(PlanTaskMode.Utilities, true, 0, true)]
    [InlineData(PlanTaskMode.Utilities, true, 1, false)]
    [InlineData(PlanTaskMode.Utilities, false, 0, false)]
    [InlineData(PlanTaskMode.Story, true, 0, false)]
    public void OpportunisticUtilityRetryAllowsOnlyOneAttemptAtATaskBoundary(
        PlanTaskMode mode,
        bool taskSwitchAvailable,
        int attempts,
        bool expected) =>
        Assert.Equal(
            expected,
            MacroUnattendedRecoveryPolicy.CanAttemptOpportunistically(mode, taskSwitchAvailable, attempts));

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
    public async Task PreflightRejectsUnimplementedEventRouteBeforeTaskValidation()
    {
        PlanTaskPrototype unsupported = new()
        {
            Mode = PlanTaskMode.Event,
            Route = "Boss Bounty",
        };
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
        PlanTaskPrototype eventTask = new()
        {
            Mode = PlanTaskMode.Event,
            Route = "Villain Invasion · Act 4",
        };
        PlanPrototype plan = new("supported", [story, raid, eventTask]);
        List<PlanTaskPrototype> validated = [];

        await MacroPlanPreflight.ValidateAsync(
            plan,
            (task, _) => { validated.Add(task); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal([story, raid, eventTask], validated);
    }

    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            SynchronizationContext? previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            finally { SetSynchronizationContext(previous); }
        }
    }
}
