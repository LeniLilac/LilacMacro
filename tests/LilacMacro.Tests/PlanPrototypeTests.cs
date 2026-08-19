using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class PlanPrototypeTests
{
    [Fact]
    public void RenamingPlanNotifiesSelectorBindings()
    {
        PlanPrototype plan = new("Plan 5", []);
        string? changedProperty = null;
        plan.PropertyChanged += (_, eventArgs) => changedProperty = eventArgs.PropertyName;

        plan.Name = "test";

        Assert.Equal(nameof(PlanPrototype.Name), changedProperty);
        Assert.Equal("test", plan.Name);
    }

    [Fact]
    public void AssigningSamePlanNameDoesNotNotify()
    {
        PlanPrototype plan = new("Plan 5", []);
        int notifications = 0;
        plan.PropertyChanged += (_, _) => notifications++;

        plan.Name = "Plan 5";

        Assert.Equal(0, notifications);
    }
}
