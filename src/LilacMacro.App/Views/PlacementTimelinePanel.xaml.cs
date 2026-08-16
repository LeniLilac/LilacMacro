using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LilacMacro.App.Notifications;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlacementTimelinePanel : UserControl
{
    private readonly ListBoxReorderDragController<PlacementStepRowViewModel> _dragController;
    private PlacementEditorSession? _session;
    private IReadOnlyList<PlacementStepRowViewModel> _rows = [];
    private bool _poppedOut;

    public PlacementTimelinePanel()
    {
        InitializeComponent();
        _dragController = new ListBoxReorderDragController<PlacementStepRowViewModel>(TimelineList);
        _dragController.ReorderRequested += DragController_OnReorderRequested;
    }

    public event EventHandler? SetupChanged;

    public event EventHandler? PopOutRequested;

    public event EventHandler? TestSetupRequested;

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

    public void SetTestState(bool running, string? status = null)
    {
        if (status is not null) PlaybackStatusText.Text = status;
        TestSetupText.Text = running ? "STOP TEST" : "TEST SETUP";
        TestSetupIcon.Data = (Geometry)FindResource(running ? "Lucide.Square" : "Lucide.Play");
        TestSetupButton.Style = (Style)FindResource(
            running ? "DangerButtonStyle" : "PrimaryButtonStyle");
        AddStepButton.IsEnabled = !running && _session?.CanEdit == true;
        TimelineList.IsHitTestVisible = !running && _session?.CanEdit == true;
        SettingsPanel.IsEnabled = !running;
    }

    public void SetTestStatus(string status) => PlaybackStatusText.Text = status;

    public void SelectStep(Guid stepId)
    {
        PlacementStepRowViewModel? row = _rows.FirstOrDefault(candidate => candidate.Step.Id == stepId);
        if (row is null) return;
        TimelineList.SelectedItem = row;
        TimelineList.ScrollIntoView(row);
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
        if (!BetweenUpgradeAttemptsText.IsKeyboardFocusWithin)
        {
            BetweenUpgradeAttemptsText.Text = _session.CurrentRoute.BetweenUpgradeAttemptsMilliseconds
                .ToString(CultureInfo.InvariantCulture);
        }
        AddStepButton.IsEnabled = _session.CanEdit;
        TimelineList.IsHitTestVisible = _session.CanEdit;
    }

    private void PopOutButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        PopOutRequested?.Invoke(this, EventArgs.Empty);

    private void TestSetupButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        TestSetupRequested?.Invoke(this, EventArgs.Empty);

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
        AddStepButton.Visibility = showSteps ? Visibility.Visible : Visibility.Collapsed;
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
        ScrollViewer? pageScroller = FindAncestorScrollViewer(this);
        double pageOffset = pageScroller?.VerticalOffset ?? 0;
        PlacementStepEditorDialog dialog = new(row, _session.CurrentRoute.Steps);
        Window? owner = Window.GetWindow(this);
        if (owner is not null) dialog.Owner = owner;
        if (dialog.ShowDialog() != true || dialog.Replacement is not { } replacement) return;
        await RunEditAsync(() => _session.ReplaceStepAsync(row.Index, replacement));
        if (pageScroller is not null)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => pageScroller.ScrollToVerticalOffset(pageOffset)));
        }
    }

    private void StepDragHandle_OnPreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs eventArgs)
    {
        if (sender is FrameworkElement { DataContext: PlacementStepRowViewModel row } rowElement &&
            !OriginatesInsideButton(eventArgs.OriginalSource as DependencyObject, rowElement))
        {
            _dragController.Begin(row, eventArgs);
        }
    }

    private static bool OriginatesInsideButton(DependencyObject? origin, DependencyObject row)
    {
        for (DependencyObject? current = origin;
             current is not null && !ReferenceEquals(current, row);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase) return true;
        }
        return false;
    }

    private void TimelineList_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs eventArgs) =>
        _dragController.Continue(eventArgs);

    private void TimelineList_OnPreviewMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs eventArgs) => _dragController.Complete(eventArgs);

    private void TimelineList_OnLostMouseCapture(object sender, MouseEventArgs eventArgs) =>
        _dragController.Cancel();

    private async void DragController_OnReorderRequested(
        object? sender,
        ListReorderEventArgs<PlacementStepRowViewModel> eventArgs)
    {
        if (_session is null) return;
        int destination = ListReorderDestination.Resolve(
            eventArgs.Source.Index,
            eventArgs.Target.Index,
            eventArgs.InsertAfter,
            _rows.Count);
        if (eventArgs.Source.Index == destination) return;
        await RunEditAsync(() => _session.MoveStepToAsync(eventArgs.Source.Index, destination));
    }

    private async void DeleteStep_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_session is null || sender is not Button { Tag: PlacementStepRowViewModel row }) return;
        await RunEditAsync(() => _session.DeleteStepAsync(row.Index));
    }

    private async void BetweenUpgradeAttempts_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs) => await SaveBetweenUpgradeAttemptsAsync();

    private async void BetweenUpgradeAttempts_OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter) return;
        eventArgs.Handled = true;
        await SaveBetweenUpgradeAttemptsAsync();
    }

    private async Task SaveBetweenUpgradeAttemptsAsync()
    {
        if (_session is null) return;
        if (!int.TryParse(
                BetweenUpgradeAttemptsText.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int milliseconds))
        {
            ShowError("Between upgrades must be a whole number.");
            BetweenUpgradeAttemptsText.Text = _session.CurrentRoute.BetweenUpgradeAttemptsMilliseconds
                .ToString(CultureInfo.InvariantCulture);
            return;
        }
        await RunEditAsync(() => _session.SetBetweenUpgradeAttemptsAsync(milliseconds));
    }

    private static void ClearError() { }

}
