using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LilacMacro.App.Notifications;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlacementTimelinePanel : UserControl
{
    private readonly ListBoxReorderDragController<PlacementStepRowViewModel> _dragController;
    private PlacementEditorSession? _session;
    private IReadOnlyList<PlacementStepRowViewModel> _rows = [];
    private bool _refreshingDefaults;
    private bool _poppedOut;

    public PlacementTimelinePanel()
    {
        InitializeComponent();
        TeamCombo.ItemsSource = Enumerable.Range(1, 8)
            .Select(team => new PlacementNumberOption(team, $"TEAM {team}"))
            .ToArray();
        _dragController = new ListBoxReorderDragController<PlacementStepRowViewModel>(TimelineList);
        _dragController.ReorderRequested += DragController_OnReorderRequested;
    }

    public event EventHandler? SetupChanged;

    public event EventHandler? PopOutRequested;

    public void Load(PlacementEditorSession session)
    {
        _session = session;
        ClearError();
        Refresh();
    }

    public void ShowError(string message)
    {
        AppToastService.ShowError("PLACEMENT ERROR", message);
    }

    public void SetPoppedOut(bool poppedOut)
    {
        _poppedOut = poppedOut;
        PopOutButton.Content = poppedOut ? "DOCK" : "POPOUT";
        ScrollViewer.SetVerticalScrollBarVisibility(
            TimelineList,
            poppedOut ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        SettingsScrollViewer.VerticalScrollBarVisibility =
            poppedOut ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private void TimelinePanel_OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (_poppedOut || FindAncestorScrollViewer(this) is not { } pageScroller) return;

        pageScroller.ScrollToVerticalOffset(pageScroller.VerticalOffset - eventArgs.Delta);
        eventArgs.Handled = true;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject origin)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(origin);
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer) return scrollViewer;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void Refresh()
    {
        if (_session is null || _session.Document is null) return;
        _rows = PlacementStepRowFactory.Create(_session.CurrentRoute);
        TimelineList.ItemsSource = _rows;
        AddStepButton.IsEnabled = _session.CanEdit;
        TimelineList.IsHitTestVisible = _session.CanEdit;
        _refreshingDefaults = true;
        try
        {
            PlacementRouteSetup route = _session.CurrentRoute;
            TeamCombo.SelectedValue = route.TeamSlot;
            SetSelectedUnit(route.SelectedUnitSlot);
        }
        finally
        {
            _refreshingDefaults = false;
        }
    }

    private void PopOutButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        PopOutRequested?.Invoke(this, EventArgs.Empty);

    private void StepsTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowSection(showSteps: true);

    private void SettingsTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowSection(showSteps: false);

    private void AdvancedSettingsButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        bool expand = AdvancedSettingsFields.Visibility != Visibility.Visible;
        AdvancedSettingsFields.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        AdvancedSettingsChevron.Data = (System.Windows.Media.Geometry)FindResource(
            expand ? "Lucide.ChevronUp" : "Lucide.ChevronDown");
    }

    private void ShowSection(bool showSteps)
    {
        StepsPanel.Visibility = showSteps ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = showSteps ? Visibility.Collapsed : Visibility.Visible;
        StepsTabButton.Tag = showSteps ? "Active" : null;
        SettingsTabButton.Tag = showSteps ? null : "Active";
    }

    private async Task RunEditAsync(Func<Task> edit)
    {
        ClearError();
        try
        {
            await edit();
            Refresh();
            SetupChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or InvalidOperationException or ArgumentException)
        {
            ShowError(exception.Message);
            Refresh();
        }
    }

    private async void AddStepButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_session is null) return;
        PlacementStep? selectedStep = (TimelineList.SelectedItem as PlacementStepRowViewModel)?.Step;
        PlacementStepEditorDialog dialog = new(
            _session.CurrentRoute,
            _session.CurrentRoute.Steps,
            selectedStep);
        Window? owner = Window.GetWindow(this);
        if (owner is not null) dialog.Owner = owner;
        if (dialog.ShowDialog() != true || dialog.Replacement is not { } step) return;

        await RunEditAsync(() => _session.AddStepAsync(step));
    }

    private async void EditStep_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_session is null || sender is not Button { Tag: PlacementStepRowViewModel row } || !row.CanEdit) return;
        PlacementStepEditorDialog dialog = new(row, _session.CurrentRoute.Steps);
        Window? owner = Window.GetWindow(this);
        if (owner is not null) dialog.Owner = owner;
        if (dialog.ShowDialog() != true || dialog.Replacement is not { } replacement) return;
        await RunEditAsync(() => _session.ReplaceStepAsync(row.Index, replacement));
    }

    private void StepDragHandle_OnPreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs eventArgs)
    {
        if (sender is FrameworkElement { DataContext: PlacementStepRowViewModel row })
        {
            _dragController.Begin(row, eventArgs);
        }
    }

    private void TimelineList_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs eventArgs) =>
        _dragController.Continue(eventArgs);

    private void TimelineList_OnPreviewMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs eventArgs) => _dragController.Cancel();

    private void TimelineList_OnDragOver(object sender, DragEventArgs eventArgs) =>
        _dragController.DragOver(eventArgs);

    private void TimelineList_OnDragLeave(object sender, DragEventArgs eventArgs) =>
        _dragController.DragLeave();

    private void TimelineList_OnDrop(object sender, DragEventArgs eventArgs) =>
        _dragController.Drop(eventArgs);

    private async void DragController_OnReorderRequested(
        object? sender,
        ListReorderEventArgs<PlacementStepRowViewModel> eventArgs)
    {
        if (_session is null) return;
        int destination = eventArgs.Target.Index + (eventArgs.InsertAfter ? 1 : 0);
        if (eventArgs.Source.Index < destination) destination--;
        if (eventArgs.Source.Index == destination) return;
        await RunEditAsync(() => _session.MoveStepToAsync(eventArgs.Source.Index, destination));
    }

    private async void DeleteStep_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_session is null || sender is not Button { Tag: PlacementStepRowViewModel row }) return;
        await RunEditAsync(() => _session.DeleteStepAsync(row.Index));
    }

    private static void ClearError() { }

    private async void TeamCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        await SaveDefaultsAsync();

    private async void UnitButton_OnChecked(object sender, RoutedEventArgs eventArgs) =>
        await SaveDefaultsAsync();

    private async Task SaveDefaultsAsync()
    {
        if (_refreshingDefaults || _session is null || TeamCombo.SelectedValue is not int teamSlot) return;
        PlacementRouteSetup route = _session.CurrentRoute;
        await RunEditAsync(() => _session.SetRouteDefaultsAsync(
            teamSlot,
            SelectedUnitSlot(),
            route.DefaultStepDelayMilliseconds,
            route.DefaultTargetingPriority,
            route.DefaultAutoUpgradePriority));
    }

    private int SelectedUnitSlot() => new[]
        {
            Unit1Button,
            Unit2Button,
            Unit3Button,
            Unit4Button,
            Unit5Button,
            Unit6Button,
        }
        .FirstOrDefault(button => button.IsChecked == true)?.Tag is string tag && int.TryParse(tag, out int slot)
            ? slot
            : 1;

    private void SetSelectedUnit(int slot)
    {
        RadioButton button = slot switch
        {
            2 => Unit2Button,
            3 => Unit3Button,
            4 => Unit4Button,
            5 => Unit5Button,
            6 => Unit6Button,
            _ => Unit1Button,
        };
        button.IsChecked = true;
    }

}
