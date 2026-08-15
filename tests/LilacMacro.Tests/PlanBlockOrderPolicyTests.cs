using System.Collections.ObjectModel;
using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class PlanBlockOrderPolicyTests
{
    [Fact]
    public void TaskDroppedOnLoopMovesInsideLoop()
    {
        PlanTaskPrototype task = new();
        PlanLoopPrototype loop = new();
        ObservableCollection<PlanBlockPrototype> root = [task, loop];

        Assert.True(PlanBlockOrderPolicy.Move(
            root,
            task,
            loop,
            insertAfter: false,
            dropOnTarget: true));

        Assert.DoesNotContain(task, root);
        Assert.Same(task, Assert.Single(loop.Children));
    }

    [Fact]
    public void ForeverLoopRemainsAfterEveryTopLevelTask()
    {
        PlanLoopPrototype forever = new() { Forever = true };
        PlanTaskPrototype first = new();
        PlanTaskPrototype second = new();
        ObservableCollection<PlanBlockPrototype> root = [first, forever, second];

        Assert.True(PlanBlockOrderPolicy.NormalizeRoot(root));

        Assert.Equal([first, second, forever], root);
    }

    [Fact]
    public void PlanLevelTaskIsInsertedBeforeForeverTail()
    {
        PlanLoopPrototype forever = new() { Forever = true };
        PlanTaskPrototype task = new();
        ObservableCollection<PlanBlockPrototype> root = [forever];

        PlanBlockOrderPolicy.AddAtPlanLevel(root, task);

        Assert.Equal([task, forever], root);
    }

    [Fact]
    public void PlanLevelLoopIsInsertedBeforeForeverTail()
    {
        PlanLoopPrototype forever = new() { Forever = true };
        PlanLoopPrototype finite = new() { Forever = false };
        ObservableCollection<PlanBlockPrototype> root = [forever];

        PlanBlockOrderPolicy.AddAtPlanLevel(root, finite);

        Assert.Equal([finite, forever], root);
    }

    [Fact]
    public void TaskCannotRemainAfterForeverLoop()
    {
        PlanTaskPrototype first = new();
        PlanLoopPrototype forever = new() { Forever = true };
        PlanTaskPrototype moved = new();
        ObservableCollection<PlanBlockPrototype> root = [first, forever, moved];

        Assert.True(PlanBlockOrderPolicy.Move(root, moved, forever, insertAfter: true));

        Assert.Equal([first, moved, forever], root);
    }

    [Fact]
    public void ForeverLoopCannotBeDraggedAboveTask()
    {
        PlanTaskPrototype task = new();
        PlanLoopPrototype forever = new() { Forever = true };
        ObservableCollection<PlanBlockPrototype> root = [task, forever];

        Assert.False(PlanBlockOrderPolicy.Move(root, forever, task, insertAfter: false));

        Assert.Equal([task, forever], root);
    }
}
