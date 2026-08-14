using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

public partial class PlanPage : UserControl
{
    private static readonly string[] StoryRoutes =
    [
        "School Grounds · Act 1", "School Grounds · Act 2", "School Grounds · Act 3",
        "School Grounds · Act 4", "School Grounds · Act 5", "School Grounds · Infinite",
        "School Grounds · Mastery", "Flower Forest · Act 1", "Rose Kingdom · Act 1",
        "Fairy King Forest · Act 1", "King's Tomb · Mastery", "East Town · Act 1",
    ];
    private static readonly string[] RaidRoutes = ["Spirit City · Act 1", "Spirit City · Act 2", "Spirit City · Act 3"];
    private static readonly string[] ExpeditionRoutes = ["School Grounds", "Flower Forest", "Rose Kingdom", "East Town"];
    private static readonly string[] EventRoutes =
    [
        "Villain Invasion · Act 1", "Villain Invasion · Act 2", "Villain Invasion · Act 3",
        "Villain Invasion · Act 4",
    ];
    private static readonly string[] UtilityRoutes =
    [
        ResourceRefuelPolicy.GoldMineRoute,
        ResourceRefuelPolicy.ResourceDrillRoute,
        ShopPurchasePolicy.GoldRoute,
        ShopPurchasePolicy.RaidRoute,
        UtilityTaskPolicy.CalendarClaimRoute,
    ];

    private readonly ObservableCollection<PlanPrototype> _plans;
    private readonly MacroOwnerState _ownerState;
    private readonly Dictionary<ListBox, ListBoxReorderDragController<PlanBlockPrototype>> _dragControllers = [];
    private PlanPrototype _selectedPlan;
    private PlanTaskPrototype? _editingTask;
    private PlanLoopPrototype? _editingLoop;
    private PlanTaskMode _editorMode = PlanTaskMode.Challenge;
    private bool _initialized;
    private IReadOnlyList<string> _pendingShopSelection = [];

    internal PlanPage(MacroOwnerState ownerState)
    {
        _ownerState = ownerState;
        _plans = ownerState.Plans;
        _selectedPlan = ownerState.SelectedPlan;
        InitializeComponent();
        PlanSelector.DisplayMemberPath = nameof(PlanPrototype.Name);
        PlanSelector.ItemsSource = _plans;
        TaskDifficultyCombo.ItemsSource = new[] { "Difficulty 1", "Difficulty 2", "Difficulty 3" };
        TaskRewardTargetCombo.ItemsSource = new[] { "None", "Fuel Cell", "Equipment Scrap", "Equipment Reroll", "Equipment Lock", "Expedition Coin" };
        _initialized = true;
        PlanSelector.SelectedItem = _selectedPlan;
        _ownerState.SelectedPlanChanged += OwnerState_OnSelectedPlanChanged;
        BindSelectedPlan();
    }

    private void PlanSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || PlanSelector.SelectedItem is not PlanPrototype plan) return;
        _selectedPlan = plan;
        _ownerState.SelectPlan(plan);
        BindSelectedPlan();
    }

    private void OwnerState_OnSelectedPlanChanged(object? sender, EventArgs eventArgs)
    {
        if (!ReferenceEquals(PlanSelector.SelectedItem, _ownerState.SelectedPlan))
            PlanSelector.SelectedItem = _ownerState.SelectedPlan;
    }

    private void BindSelectedPlan()
    {
        DataContext = _selectedPlan;
        PlanNameText.Text = _selectedPlan.Name;
        Reindex();
        MarkSelected(_selectedPlan.Blocks.FirstOrDefault());
    }

    private void PlanNameText_OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        string name = PlanNameText.Text.Trim();
        if (name.Length == 0 || string.Equals(name, _selectedPlan.Name, StringComparison.Ordinal)) return;
        _selectedPlan.Name = name;
        PlanSelector.Items.Refresh();
        _ownerState.NotifyPlansChanged();
    }

    private void NewPlan_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        PlanPrototype plan = new($"Plan {_plans.Count + 1}", []);
        _plans.Add(plan);
        PlanSelector.SelectedItem = plan;
        _ownerState.NotifyPlansChanged();
    }

    private void CopyPlan_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        const int suffixLength = 5;
        string copyName = $"{_selectedPlan.Name[..Math.Min(_selectedPlan.Name.Length, 100 - suffixLength)]} copy";
        PlanPrototype copy = _selectedPlan.Clone(copyName);
        _plans.Add(copy);
        PlanSelector.SelectedItem = copy;
        _ownerState.NotifyPlansChanged();
    }

    private void DeletePlan_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_plans.Count == 1)
        {
            AppToastService.ShowError("PLAN REQUIRED", "At least one plan must remain.");
            return;
        }

        int index = _plans.IndexOf(_selectedPlan);
        _plans.Remove(_selectedPlan);
        PlanSelector.SelectedItem = _plans[Math.Min(index, _plans.Count - 1)];
        _ownerState.NotifyPlansChanged();
    }

    private void ResetProgress_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
    }

    private void AddTask_OnClick(object sender, RoutedEventArgs eventArgs) => OpenTaskEditor(null);

    private void AddTaskToLoop_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: PlanLoopPrototype loop }) OpenTaskEditor(loop);
    }

    private void OpenTaskEditor(PlanLoopPrototype? destination)
    {
        _editingTask = null;
        TaskEditorTitle.Text = "ADD TASK";
        PopulateDestinations(destination);
        TaskTargetText.Text = "15";
        TaskRetriesText.Text = "0";
        TaskDifficultyCombo.SelectedIndex = 0;
        TaskBossNodesText.Text = "1";
        TaskExtractCheck.IsChecked = true;
        TaskRewardTargetCombo.SelectedIndex = 0;
        TaskHardModeCheck.IsChecked = false;
        TaskTraitCheck.IsChecked = true;
        TaskStatCheck.IsChecked = true;
        TaskSpriteCheck.IsChecked = true;
        _pendingShopSelection = [];
        ChallengeModeButton.IsChecked = true;
        TaskEditorOverlay.Visibility = Visibility.Visible;
    }

    private void EditTask_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: PlanTaskPrototype task }) return;
        _editingTask = task;
        TaskEditorTitle.Text = $"EDIT TASK {task.Priority}";
        PopulateDestinations(FindParentLoop(task));
        SetEditorMode(task.Mode);
        TaskRouteCombo.SelectedItem = task.Route;
        TaskTargetText.Text = task.Target.ToString();
        TaskRetriesText.Text = task.DefeatRetries.ToString();
        TaskDifficultyCombo.SelectedIndex = task.Difficulty - 1;
        TaskBossNodesText.Text = task.BossesBeforeExtract.ToString();
        TaskExtractCheck.IsChecked = task.ExtractAtCheckpoint;
        TaskRewardTargetCombo.SelectedItem = task.RewardTarget;
        TaskHardModeCheck.IsChecked = task.HardMode;
        TaskTraitCheck.IsChecked = task.RunTrait;
        TaskStatCheck.IsChecked = task.RunStat;
        TaskSpriteCheck.IsChecked = task.RunSprite;
        _pendingShopSelection = task.ShopItemIds;
        RefreshShopItemEditor();
        TaskEditorOverlay.Visibility = Visibility.Visible;
    }

    private void ApplyTaskEditor_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        bool fixedReset = _editorMode == PlanTaskMode.Utilities && UtilityTaskPolicy.UsesFixedUtcReset(
            TaskRouteCombo.SelectedItem as string ?? string.Empty);
        if (!int.TryParse(TaskTargetText.Text, out int target) && _editorMode != PlanTaskMode.Challenge && !fixedReset)
        {
            AppToastService.ShowError("INVALID TARGET", "Enter a whole-number target.");
            return;
        }
        if (!int.TryParse(TaskRetriesText.Text, out int retries)) retries = 0;
        if (!int.TryParse(TaskBossNodesText.Text, out int bosses)) bosses = 1;

        PlanTaskPrototype task = _editingTask ?? new PlanTaskPrototype();
        task.Mode = _editorMode;
        task.Route = TaskRouteCombo.SelectedItem as string ?? DefaultRoute(_editorMode);
        string[] selectedShopItems = TaskShopItemsList.ItemsSource is IEnumerable<ShopItemChoice> choices
            ? choices.Where(choice => choice.IsSelected).Select(choice => choice.Id).ToArray()
            : [];
        if (ShopPurchasePolicy.IsShopRoute(task.Route) && selectedShopItems.Length == 0)
        {
            AppToastService.ShowError("ITEM REQUIRED", "Enable at least one shop item.");
            return;
        }
        task.Target = _editorMode switch
        {
            PlanTaskMode.Challenge => 1,
            _ when fixedReset => 1,
            _ => Math.Max(1, target),
        };
        task.DefeatRetries = retries;
        task.Difficulty = TaskDifficultyCombo.SelectedIndex + 1;
        task.BossesBeforeExtract = bosses;
        task.ExtractAtCheckpoint = TaskExtractCheck.IsChecked == true;
        task.RewardTarget = TaskRewardTargetCombo.SelectedItem as string ?? "None";
        task.HardMode = TaskHardModeCheck.IsChecked == true;
        task.RunTrait = TaskTraitCheck.IsChecked == true;
        task.RunStat = TaskStatCheck.IsChecked == true;
        task.RunSprite = TaskSpriteCheck.IsChecked == true;
        task.ShopItemIds = selectedShopItems;

        ObservableCollection<PlanBlockPrototype> destination = SelectedDestination()?.Children ?? _selectedPlan.Blocks;
        ObservableCollection<PlanBlockPrototype>? current = FindOwnerCollection(task);
        if (current is null) destination.Add(task);
        else if (!ReferenceEquals(current, destination))
        {
            current.Remove(task);
            destination.Add(task);
        }

        Reindex();
        MarkSelected(task);
        _ownerState.NotifyPlansChanged();
        CloseTaskEditor();
    }

    private void CloseTaskEditor_OnClick(object sender, RoutedEventArgs eventArgs) => CloseTaskEditor();

    private void CloseTaskEditor()
    {
        TaskEditorOverlay.Visibility = Visibility.Collapsed;
        _editingTask = null;
    }

    private void TaskModeButton_OnChecked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized || sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out PlanTaskMode mode)) return;
        _editorMode = mode;
        UpdateTaskEditorForMode();
    }

    private void SetEditorMode(PlanTaskMode mode)
    {
        RadioButton button = mode switch
        {
            PlanTaskMode.Expedition => ExpeditionModeButton,
            PlanTaskMode.Story => StoryModeButton,
            PlanTaskMode.Raid => RaidModeButton,
            PlanTaskMode.Event => EventModeButton,
            PlanTaskMode.Utilities => UtilitiesModeButton,
            _ => ChallengeModeButton,
        };
        button.IsChecked = true;
    }

    private void UpdateTaskEditorForMode()
    {
        bool challenge = _editorMode == PlanTaskMode.Challenge;
        bool utility = _editorMode == PlanTaskMode.Utilities;
        bool expedition = _editorMode == PlanTaskMode.Expedition;
        bool story = _editorMode == PlanTaskMode.Story;
        TaskSchedulePanel.Visibility = challenge ? Visibility.Collapsed : Visibility.Visible;
        TaskOptionsPanel.Visibility = utility ? Visibility.Collapsed : Visibility.Visible;
        TaskOptionsPanel.Margin = challenge ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        TaskRetriesPanel.Visibility = _editorMode is PlanTaskMode.Challenge or PlanTaskMode.Story or PlanTaskMode.Raid or PlanTaskMode.Event ? Visibility.Visible : Visibility.Collapsed;
        TaskChallengePanel.Visibility = challenge ? Visibility.Visible : Visibility.Collapsed;
        TaskExpeditionPanel.Visibility = expedition ? Visibility.Visible : Visibility.Collapsed;
        TaskStoryPanel.Visibility = story ? Visibility.Visible : Visibility.Collapsed;
        TaskTargetLabel.Text = utility ? "INTERVAL, MIN" : "VICTORIES";
        TaskRouteCombo.ItemsSource = RoutesFor(_editorMode);
        TaskRouteCombo.SelectedIndex = 0;
        if (utility) TaskTargetText.Text = "60";
        RefreshShopItemEditor();
    }

    private void TaskRouteCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        RefreshShopItemEditor();
    }

    private void RefreshShopItemEditor()
    {
        if (TaskRouteCombo is null || TaskShopItemsPanel is null) return;
        string route = TaskRouteCombo.SelectedItem as string ?? string.Empty;
        bool shop = _editorMode == PlanTaskMode.Utilities && ShopPurchasePolicy.IsShopRoute(route);
        TaskShopItemsPanel.Visibility = shop ? Visibility.Visible : Visibility.Collapsed;
        TaskTargetPanel.Visibility = _editorMode == PlanTaskMode.Utilities && UtilityTaskPolicy.UsesFixedUtcReset(route)
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (!shop)
        {
            TaskShopItemsList.ItemsSource = null;
            return;
        }
        HashSet<string> selected = new(_pendingShopSelection, StringComparer.Ordinal);
        TaskShopItemsList.ItemsSource = ShopPurchasePolicy.ItemsFor(route)
            .Select(item => new ShopItemChoice(item.Id, item.DisplayName, selected.Contains(item.Id)))
            .ToArray();
        _pendingShopSelection = [];
    }

    private void AddLoop_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        PlanLoopPrototype loop = new() { Label = $"Loop {AllLoops().Count + 1}" };
        _selectedPlan.Blocks.Add(loop);
        MarkSelected(loop);
        _ownerState.NotifyPlansChanged();
        OpenLoopEditor(loop);
    }

    private void EditLoop_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: PlanLoopPrototype loop }) OpenLoopEditor(loop);
    }

    private void OpenLoopEditor(PlanLoopPrototype loop)
    {
        _editingLoop = loop;
        LoopEditorTitle.Text = $"{loop.Label.ToUpperInvariant()} SETTINGS";
        LoopForeverCheck.IsChecked = loop.Forever;
        LoopRepeatCountText.Text = loop.RepeatCount.ToString();
        LoopEditorOverlay.Visibility = Visibility.Visible;
        UpdateRepeatCountState();
    }

    private void ApplyLoopEditor_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_editingLoop is null) return;
        bool forever = LoopForeverCheck.IsChecked == true;
        if (!int.TryParse(LoopRepeatCountText.Text, out int count) || count is < 1 or > 100000)
        {
            if (!forever)
            {
                AppToastService.ShowError("INVALID REPEAT COUNT", "Enter a value from 1 to 100000.");
                return;
            }
            count = 2;
        }
        _editingLoop.Forever = forever;
        _editingLoop.RepeatCount = count;
        _ownerState.NotifyPlansChanged();
        CloseLoopEditor();
    }

    private void CloseLoopEditor_OnClick(object sender, RoutedEventArgs eventArgs) => CloseLoopEditor();

    private void CloseLoopEditor()
    {
        LoopEditorOverlay.Visibility = Visibility.Collapsed;
        _editingLoop = null;
    }

    private void LoopForeverCheck_OnChanged(object sender, RoutedEventArgs eventArgs) => UpdateRepeatCountState();

    private void UpdateRepeatCountState()
    {
        if (LoopRepeatCountText is not null) LoopRepeatCountText.IsEnabled = LoopForeverCheck.IsChecked != true;
    }

    private void DeleteTask_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: PlanTaskPrototype task }) return;
        FindOwnerCollection(task)?.Remove(task);
        Reindex();
        MarkSelected(_selectedPlan.Blocks.FirstOrDefault());
        _ownerState.NotifyPlansChanged();
    }

    private void DeleteLoop_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: PlanLoopPrototype loop }) return;
        ObservableCollection<PlanBlockPrototype>? owner = FindOwnerCollection(loop);
        if (owner is null) return;
        int index = owner.IndexOf(loop);
        owner.RemoveAt(index);
        foreach (PlanBlockPrototype child in loop.Children.Reverse()) owner.Insert(index, child);
        Reindex();
        _ownerState.NotifyPlansChanged();
    }

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

    private void BlockList_OnPreviewMouseMove(object sender, MouseEventArgs eventArgs) => Controller(sender)?.Continue(eventArgs);
    private void BlockList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs) => Controller(sender)?.Complete(eventArgs);
    private void BlockList_OnLostMouseCapture(object sender, MouseEventArgs eventArgs) => Controller(sender)?.Cancel();

    private ListBoxReorderDragController<PlanBlockPrototype>? Controller(object sender) =>
        sender is ListBox list ? EnsureDragController(list) : null;

    private ListBoxReorderDragController<PlanBlockPrototype> EnsureDragController(ListBox list)
    {
        if (_dragControllers.TryGetValue(list, out ListBoxReorderDragController<PlanBlockPrototype>? controller)) return controller;
        controller = new ListBoxReorderDragController<PlanBlockPrototype>(list);
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

    private void DragController_OnReorderRequested(object? sender, ListReorderEventArgs<PlanBlockPrototype> eventArgs)
    {
        ObservableCollection<PlanBlockPrototype>? sourceOwner = FindOwnerCollection(eventArgs.Source);
        ObservableCollection<PlanBlockPrototype>? targetOwner = FindOwnerCollection(eventArgs.Target);
        if (sourceOwner is null || targetOwner is null || eventArgs.Source is PlanLoopPrototype loop && Owns(loop, targetOwner)) return;
        int destination = targetOwner.IndexOf(eventArgs.Target) + (eventArgs.InsertAfter ? 1 : 0);
        int sourceIndex = sourceOwner.IndexOf(eventArgs.Source);
        if (ReferenceEquals(sourceOwner, targetOwner) && sourceIndex < destination) destination--;
        sourceOwner.Remove(eventArgs.Source);
        targetOwner.Insert(Math.Clamp(destination, 0, targetOwner.Count), eventArgs.Source);
        Reindex();
        _ownerState.NotifyPlansChanged();
    }

    private void PopulateDestinations(PlanLoopPrototype? selected)
    {
        PlanDestinationChoice[] choices =
        [
            new("Plan level", null),
            .. AllLoops().Select(loop => new PlanDestinationChoice(loop.Label, loop)),
        ];
        TaskDestinationCombo.ItemsSource = choices;
        TaskDestinationCombo.SelectedItem = choices.First(choice => ReferenceEquals(choice.Loop, selected));
    }

    private PlanLoopPrototype? SelectedDestination() => (TaskDestinationCombo.SelectedItem as PlanDestinationChoice)?.Loop;

    private void Reindex()
    {
        int priority = 1;
        foreach (PlanTaskPrototype task in TasksIn(_selectedPlan.Blocks)) task.Priority = priority++;
    }

    private void MarkSelected(PlanBlockPrototype? selected)
    {
        foreach (PlanBlockPrototype block in BlocksIn(_selectedPlan.Blocks)) block.IsSelected = ReferenceEquals(block, selected);
    }

    private ObservableCollection<PlanBlockPrototype>? FindOwnerCollection(PlanBlockPrototype target) =>
        FindOwnerCollection(_selectedPlan.Blocks, target);

    private static ObservableCollection<PlanBlockPrototype>? FindOwnerCollection(ObservableCollection<PlanBlockPrototype> blocks, PlanBlockPrototype target)
    {
        if (blocks.Contains(target)) return blocks;
        foreach (PlanLoopPrototype loop in blocks.OfType<PlanLoopPrototype>())
        {
            ObservableCollection<PlanBlockPrototype>? found = FindOwnerCollection(loop.Children, target);
            if (found is not null) return found;
        }
        return null;
    }

    private PlanLoopPrototype? FindParentLoop(PlanTaskPrototype task) =>
        AllLoops().FirstOrDefault(loop => loop.Children.Contains(task) || FindOwnerCollection(loop.Children, task) is not null);

    private List<PlanLoopPrototype> AllLoops() => BlocksIn(_selectedPlan.Blocks).OfType<PlanLoopPrototype>().ToList();

    private static IEnumerable<PlanBlockPrototype> BlocksIn(IEnumerable<PlanBlockPrototype> blocks)
    {
        foreach (PlanBlockPrototype block in blocks)
        {
            yield return block;
            if (block is PlanLoopPrototype loop)
                foreach (PlanBlockPrototype child in BlocksIn(loop.Children)) yield return child;
        }
    }

    private static IEnumerable<PlanTaskPrototype> TasksIn(IEnumerable<PlanBlockPrototype> blocks) => BlocksIn(blocks).OfType<PlanTaskPrototype>();
    private static bool Owns(PlanLoopPrototype loop, ObservableCollection<PlanBlockPrototype> collection) => ReferenceEquals(loop.Children, collection) || loop.Children.OfType<PlanLoopPrototype>().Any(child => Owns(child, collection));

    private static string[] RoutesFor(PlanTaskMode mode) => mode switch
    {
        PlanTaskMode.Story => StoryRoutes,
        PlanTaskMode.Raid => RaidRoutes,
        PlanTaskMode.Expedition => ExpeditionRoutes,
        PlanTaskMode.Event => EventRoutes,
        PlanTaskMode.Utilities => UtilityRoutes,
        _ => [],
    };

    private static string DefaultRoute(PlanTaskMode mode) => RoutesFor(mode).FirstOrDefault() ?? mode.ToString();

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        for (DependencyObject? node = current; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is T match) return match;
        return null;
    }
}

internal sealed class ShopItemChoice(string id, string displayName, bool isSelected)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool IsSelected { get; set; } = isSelected;
}
