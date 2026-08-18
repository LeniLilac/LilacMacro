using LilacMacro.App.Runtime;
using LilacMacro.App.Views;
using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class MacroRuntimeProgressTests
{
    [Fact]
    public async Task RuntimeProgressRoundTripsAcrossPlanReconstruction()
    {
        string root = TemporaryRoot();
        DateTimeOffset dueAt = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        try
        {
            PlanTaskPrototype eventTask = new()
            {
                Mode = PlanTaskMode.Event,
                Route = "Villain Invasion · Act 1",
                Target = 150,
            };
            PlanTaskPrototype shopTask = new()
            {
                Mode = PlanTaskMode.Utilities,
                Route = ShopPurchasePolicy.RaidRoute,
                ShopItemIds = ["trait-crystal"],
            };
            PlanLoopPrototype loop = new() { Label = "Forever", Forever = true };
            loop.Children.Add(eventTask);
            PlanPrototype plan = new("Daily", [loop, shopTask]);
            Dictionary<PlanTaskPrototype, int> victories = new() { [eventTask] = 47 };
            Dictionary<PlanTaskPrototype, int> defeats = new() { [eventTask] = 2 };
            Dictionary<PlanLoopPrototype, int> completedRuns = new() { [loop] = 3 };
            Dictionary<PlanTaskPrototype, DateTimeOffset> due = new() { [shopTask] = dueAt };

            MacroRuntimeProgressStore firstStore = new(root, "desktop");
            await firstStore.QueueSave(MacroRuntimeProgressMapper.Capture(
                [plan], victories, defeats, completedRuns, due));
            await firstStore.FlushAsync();

            PlanSettingsSnapshot snapshot = PlanPersistence.CreateSnapshot([plan]).Single();
            Assert.True(PlanPersistence.TryRestore([snapshot], out var restoredPlans));
            PlanPrototype restoredPlan = Assert.Single(restoredPlans);
            PlanLoopPrototype restoredLoop = Assert.IsType<PlanLoopPrototype>(restoredPlan.Blocks[0]);
            PlanTaskPrototype restoredEvent = Assert.IsType<PlanTaskPrototype>(restoredLoop.Children[0]);
            PlanTaskPrototype restoredShop = Assert.IsType<PlanTaskPrototype>(restoredPlan.Blocks[1]);
            Dictionary<PlanTaskPrototype, int> restoredVictories = [];
            Dictionary<PlanTaskPrototype, int> restoredDefeats = [];
            Dictionary<PlanLoopPrototype, int> restoredRuns = [];
            Dictionary<PlanTaskPrototype, DateTimeOffset> restoredDue = [];

            MacroRuntimeProgressSnapshot loaded = await new MacroRuntimeProgressStore(root, "desktop").LoadAsync();
            MacroRuntimeProgressMapper.Apply(
                [restoredPlan], loaded, restoredVictories, restoredDefeats, restoredRuns, restoredDue);

            Assert.Equal(47, restoredVictories[restoredEvent]);
            Assert.Equal(2, restoredDefeats[restoredEvent]);
            Assert.Equal(3, restoredRuns[restoredLoop]);
            Assert.Equal(3, restoredLoop.CompletedRuns);
            Assert.Equal(dueAt, restoredDue[restoredShop]);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void EmptyProgressClearsCountersLoopRunsAndUtilityDeadlines()
    {
        PlanTaskPrototype task = new() { Mode = PlanTaskMode.Event, Route = "Event", Target = 1 };
        PlanLoopPrototype loop = new() { Label = "Loop" };
        loop.Children.Add(task);
        PlanPrototype plan = new("Plan", [loop]);
        Dictionary<PlanTaskPrototype, int> victories = new() { [task] = 1 };
        Dictionary<PlanTaskPrototype, int> defeats = new() { [task] = 1 };
        Dictionary<PlanLoopPrototype, int> runs = new() { [loop] = 1 };
        Dictionary<PlanTaskPrototype, DateTimeOffset> due = new() { [task] = DateTimeOffset.UtcNow };
        loop.CompletedRuns = 1;

        MacroRuntimeProgressMapper.Apply(
            [plan], new MacroRuntimeProgressSnapshot(), victories, defeats, runs, due);

        Assert.Empty(victories);
        Assert.Empty(defeats);
        Assert.Empty(runs);
        Assert.Empty(due);
        Assert.Equal(0, loop.CompletedRuns);
    }

    private static string TemporaryRoot() =>
        Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
