using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LilacMacro.App.Views;

public partial class PlanPage
{
    private readonly Dictionary<ListBox, ListBoxReorderDragController<PlanBlockPrototype>> _dragControllers = [];

    private void BlockRow_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is Border { DataContext: PlanBlockPrototype block }) MarkSelected(block);
    }

    private void BlockList_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ListBox list) EnsureDragController(list);
    }

    private void BlockDragHandle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { Tag: PlanBlockPrototype block } element) return;
        ListBox? list = FindAncestor<ListBox>(element);
        if (list is null) return;
        MarkSelected(block);
        EnsureDragController(list).Begin(block, eventArgs);
    }

    private void BlockList_OnPreviewMouseMove(object sender, MouseEventArgs eventArgs) =>
        Controller(sender)?.Continue(eventArgs);

    private void BlockList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs) =>
        Controller(sender)?.Complete(eventArgs);

    private void BlockList_OnLostMouseCapture(object sender, MouseEventArgs eventArgs) =>
        Controller(sender)?.Cancel();

    private ListBoxReorderDragController<PlanBlockPrototype>? Controller(object sender) =>
        sender is ListBox list ? EnsureDragController(list) : null;

    private ListBoxReorderDragController<PlanBlockPrototype> EnsureDragController(ListBox list)
    {
        if (_dragControllers.TryGetValue(
                list,
                out ListBoxReorderDragController<PlanBlockPrototype>? controller))
        {
            return controller;
        }
        controller = new ListBoxReorderDragController<PlanBlockPrototype>(
            list,
            static (source, target) => source is PlanTaskPrototype && target is PlanLoopPrototype);
        controller.ReorderRequested += DragController_OnReorderRequested;
        controller.DragEnded += DragController_OnDragEnded;
        _dragControllers[list] = controller;
        return controller;
    }

    private void DragController_OnDragEnded(object? sender, EventArgs eventArgs)
    {
        MarkSelected(null);
        foreach (ListBox list in _dragControllers.Keys) list.SelectedItem = null;
    }

    private void DragController_OnReorderRequested(
        object? sender,
        ListReorderEventArgs<PlanBlockPrototype> eventArgs)
    {
        if (!PlanBlockOrderPolicy.Move(
                _selectedPlan.Blocks,
                eventArgs.Source,
                eventArgs.Target,
                eventArgs.InsertAfter,
                eventArgs.DropOnTarget))
        {
            return;
        }
        Reindex();
        _ownerState.NotifyPlansChanged();
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        for (DependencyObject? node = current; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match) return match;
        }
        return null;
    }
}
