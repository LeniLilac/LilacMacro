using System.Collections.ObjectModel;

namespace LilacMacro.App.Views;

internal static class PlanBlockOrderPolicy
{
    public static bool NormalizeRoot(ObservableCollection<PlanBlockPrototype> root)
    {
        bool changed = HoistNestedForeverLoops(root, root);
        PlanBlockPrototype[] ordered =
        [
            .. root.Where(block => block is not PlanLoopPrototype { Forever: true }),
            .. root.Where(block => block is PlanLoopPrototype { Forever: true }),
        ];
        if (root.SequenceEqual(ordered)) return changed;
        root.Clear();
        foreach (PlanBlockPrototype block in ordered) root.Add(block);
        return true;
    }

    public static void AddAtPlanLevel(ObservableCollection<PlanBlockPrototype> root, PlanBlockPrototype block)
    {
        int firstForever = root.ToList().FindIndex(item => item is PlanLoopPrototype { Forever: true });
        root.Insert(firstForever < 0 ? root.Count : firstForever, block);
        NormalizeRoot(root);
    }

    public static bool Move(
        ObservableCollection<PlanBlockPrototype> root,
        PlanBlockPrototype source,
        PlanBlockPrototype target,
        bool insertAfter,
        bool dropOnTarget = false)
    {
        ObservableCollection<PlanBlockPrototype>? sourceOwner = FindOwner(root, source);
        ObservableCollection<PlanBlockPrototype>? targetOwner = FindOwner(root, target);
        if (sourceOwner is null || targetOwner is null || source is PlanLoopPrototype { Forever: true } ||
            source is PlanLoopPrototype loop && Owns(loop, targetOwner))
        {
            return false;
        }

        if (dropOnTarget && source is PlanTaskPrototype && target is PlanLoopPrototype targetLoop)
        {
            sourceOwner.Remove(source);
            targetLoop.Children.Add(source);
            NormalizeRoot(root);
            return true;
        }

        int destination = targetOwner.IndexOf(target) + (insertAfter ? 1 : 0);
        int sourceIndex = sourceOwner.IndexOf(source);
        if (ReferenceEquals(sourceOwner, targetOwner) && sourceIndex < destination) destination--;
        sourceOwner.Remove(source);
        targetOwner.Insert(Math.Clamp(destination, 0, targetOwner.Count), source);
        NormalizeRoot(root);
        return true;
    }

    private static bool HoistNestedForeverLoops(
        ObservableCollection<PlanBlockPrototype> collection,
        ObservableCollection<PlanBlockPrototype> root)
    {
        bool changed = false;
        foreach (PlanLoopPrototype loop in collection.OfType<PlanLoopPrototype>().ToArray())
        {
            changed |= HoistNestedForeverLoops(loop.Children, root);
            if (ReferenceEquals(collection, root) || !loop.Forever) continue;
            collection.Remove(loop);
            root.Add(loop);
            changed = true;
        }
        return changed;
    }

    private static ObservableCollection<PlanBlockPrototype>? FindOwner(
        ObservableCollection<PlanBlockPrototype> blocks,
        PlanBlockPrototype target)
    {
        if (blocks.Contains(target)) return blocks;
        foreach (PlanLoopPrototype loop in blocks.OfType<PlanLoopPrototype>())
        {
            ObservableCollection<PlanBlockPrototype>? found = FindOwner(loop.Children, target);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool Owns(PlanLoopPrototype loop, ObservableCollection<PlanBlockPrototype> collection) =>
        ReferenceEquals(loop.Children, collection) ||
        loop.Children.OfType<PlanLoopPrototype>().Any(child => Owns(child, collection));
}
