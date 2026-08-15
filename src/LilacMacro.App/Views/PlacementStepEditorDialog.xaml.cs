using System.Globalization;
using System.Windows;
using System.Windows.Input;
using LilacMacro.App.Notifications;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlacementStepEditorDialog : Window
{
    private readonly PlacementStep _original;
    private readonly bool _isAdd;
    private PlacementStepKind _kind;
    private bool _ready;

    public PlacementStepEditorDialog(
        PlacementStepRowViewModel row,
        IEnumerable<PlacementStep> earlierSteps)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(earlierSteps);
        InitializeComponent();
        _original = row.Step;
        _kind = row.Step.Kind;
        DialogTitle.Text = $"EDIT {row.Title}";
        PlacementStep[] steps = earlierSteps.ToArray();
        PopulateOptions(steps, steps.Take(row.Index));
        LoadStep();
        _ready = true;
        ConfigureFields();
    }

    public PlacementStepEditorDialog(
        PlacementRouteSetup route,
        IEnumerable<PlacementStep> earlierSteps,
        PlacementStep? selectedStep)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(earlierSteps);
        InitializeComponent();
        PlacementStep[] earlier = earlierSteps.ToArray();
        PlacementStep? selectedPlacement = selectedStep?.Kind == PlacementStepKind.Place
            ? selectedStep
            : earlier.LastOrDefault(step => step.Kind == PlacementStepKind.Place);
        _isAdd = true;
        _kind = PlacementStepKind.Delay;
        _original = new PlacementStep
        {
            Kind = _kind,
            TargetPlacementId = selectedPlacement?.Id,
            UnitSlot = selectedPlacement?.UnitSlot ?? route.SelectedUnitSlot,
            TargetingPriority = route.DefaultTargetingPriority,
            AutoUpgradePriority = route.DefaultAutoUpgradePriority,
            ChangeTargetingPriority = true,
            AutoUpgradeAction = PlacementAutoUpgradeAction.NoChange,
            DelayDurationMilliseconds = 1_000,
            UpgradeCount = 1,
        };
        DialogTitle.Text = "ADD STEP";
        PopulateOptions(earlier, earlier);
        LoadStep();
        ActionFields.Visibility = Visibility.Visible;
        _ready = true;
        SelectedActionButton(_kind).IsChecked = true;
        ConfigureFields();
    }

    public PlacementStep? Replacement { get; private set; }

    private void PopulateOptions(
        IReadOnlyList<PlacementStep> allSteps,
        IEnumerable<PlacementStep> availablePlacements)
    {
        EditUnitCombo.ItemsSource = Enumerable.Range(1, 6)
            .Select(slot => new PlacementNumberOption(slot, slot.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        EditTargetingCombo.ItemsSource = EnumOptions<PlacementTargetingPriority>();
        ReconfigureTargetingCombo.ItemsSource = EnumOptions<PlacementTargetingPriority>();
        EditAutoUpgradeCombo.ItemsSource = Enum.GetValues<PlacementAutoUpgradePriority>()
            .Select(value => new PlacementOption<PlacementAutoUpgradePriority>(value, AutoUpgradeLabel(value)))
            .ToArray();
        AutoUpgradeActionCombo.ItemsSource = Enum.GetValues<PlacementAutoUpgradeAction>()
            .Select(value => new PlacementOption<PlacementAutoUpgradeAction>(value, AutoUpgradeActionLabel(value)))
            .ToArray();
        EditReferenceCombo.ItemsSource = BuildReferenceOptions(allSteps, availablePlacements);
    }

    internal static PlacementReferenceOption[] BuildReferenceOptions(
        IReadOnlyList<PlacementStep> allSteps,
        IEnumerable<PlacementStep> availablePlacements)
    {
        ArgumentNullException.ThrowIfNull(allSteps);
        ArgumentNullException.ThrowIfNull(availablePlacements);
        IReadOnlyDictionary<Guid, string> labels = PlacementReferencePolicy.BuildDisplayLabels(allSteps);
        return availablePlacements
            .Where(step => step.Kind == PlacementStepKind.Place)
            .Select(step => new PlacementReferenceOption(
                step.Id,
                labels.GetValueOrDefault(step.Id, step.UnitSlot.ToString(CultureInfo.InvariantCulture))))
            .ToArray();
    }

    private void LoadStep()
    {
        EditUnitCombo.SelectedValue = _original.UnitSlot;
        EditXText.Text = Number(_original.X);
        EditYText.Text = Number(_original.Y);
        EditTargetingCombo.SelectedValue = _original.TargetingPriority;
        EditAutoUpgradeCombo.SelectedValue = _original.AutoUpgradePriority;
        EditReferenceCombo.SelectedValue = _original.TargetPlacementId;
        ChangeTargetingCheck.IsChecked = _original.ChangeTargetingPriority;
        ReconfigureTargetingCombo.SelectedValue = _original.TargetingPriority;
        AutoUpgradeActionCombo.SelectedValue = _original.AutoUpgradeAction;
        DelayDurationText.Text = Number(_original.DelayDurationMilliseconds);
        UpgradeCountText.Text = Number(_original.UpgradeCount);
    }

    private void ConfigureFields()
    {
        PlaceFields.Visibility = Show(_kind == PlacementStepKind.Place);
        PlaceCoordinateFields.Visibility = Show(_kind == PlacementStepKind.Place && !_isAdd);
        ReferenceFields.Visibility = Show(_kind is PlacementStepKind.Reconfigure or
            PlacementStepKind.Upgrade or PlacementStepKind.Sell);
        ReconfigureFields.Visibility = Show(_kind == PlacementStepKind.Reconfigure);
        DelayFields.Visibility = Show(_kind == PlacementStepKind.Delay);
        UpgradeFields.Visibility = Show(_kind == PlacementStepKind.Upgrade);
    }

    private void Apply_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Replacement = ReadStep();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
                                           ArgumentException or FormatException or OverflowException)
        {
            AppToastService.ShowError("STEP SETTINGS", exception.Message);
        }
    }

    private PlacementStep ReadStep()
    {
        return _kind switch
        {
            PlacementStepKind.Place => StepBase(PlacementStepKind.Place) with
            {
                UnitSlot = RequiredValue<PlacementNumberOption>(EditUnitCombo, "Unit slot").Value,
                X = _isAdd ? 0 : Parse(EditXText.Text, "X"),
                Y = _isAdd ? 0 : Parse(EditYText.Text, "Y"),
                TargetingPriority = RequiredEnum<PlacementTargetingPriority>(EditTargetingCombo, "Targeting"),
                AutoUpgradePriority = RequiredEnum<PlacementAutoUpgradePriority>(EditAutoUpgradeCombo, "Auto Upgrade"),
            },
            PlacementStepKind.Reconfigure => StepBase(PlacementStepKind.Reconfigure) with
            {
                TargetPlacementId = RequiredReference(),
                ChangeTargetingPriority = ChangeTargetingCheck.IsChecked == true,
                TargetingPriority = RequiredEnum<PlacementTargetingPriority>(ReconfigureTargetingCombo, "Targeting"),
                AutoUpgradeAction = RequiredEnum<PlacementAutoUpgradeAction>(AutoUpgradeActionCombo, "Auto Upgrade"),
            },
            PlacementStepKind.Delay => StepBase(PlacementStepKind.Delay) with
            {
                DelayDurationMilliseconds = Parse(DelayDurationText.Text, "Delay duration"),
            },
            PlacementStepKind.Upgrade => StepBase(PlacementStepKind.Upgrade) with
            {
                TargetPlacementId = RequiredReference(),
                UpgradeCount = Parse(UpgradeCountText.Text, "Upgrade count"),
            },
            PlacementStepKind.Sell => StepBase(PlacementStepKind.Sell) with
            {
                TargetPlacementId = RequiredReference(),
            },
            _ => throw new InvalidOperationException("This step cannot be edited."),
        };
    }

    private PlacementStep StepBase(PlacementStepKind kind) => _isAdd
        ? new PlacementStep { Kind = kind }
        : _original with { Kind = kind };

    private Guid RequiredReference() =>
        EditReferenceCombo.SelectedValue is Guid id
            ? id
            : throw new InvalidDataException("Placement is required.");

    private static T RequiredEnum<T>(System.Windows.Controls.ComboBox combo, string label)
        where T : struct, Enum => combo.SelectedValue is T value
            ? value
            : throw new InvalidDataException($"{label} is required.");

    private static T RequiredValue<T>(System.Windows.Controls.ComboBox combo, string label)
        where T : class => combo.SelectedItem as T ?? throw new InvalidDataException($"{label} is required.");

    private static int Parse(string text, string label) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new InvalidDataException($"{label} must be a whole number.");

    private static PlacementOption<T>[] EnumOptions<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new PlacementOption<T>(value, SplitEnum(value.ToString()))).ToArray();

    private static string SplitEnum(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "(?<!^)([A-Z])", " $1");

    private static string AutoUpgradeLabel(PlacementAutoUpgradePriority value) =>
        value == PlacementAutoUpgradePriority.Off ? "Off" : $"Priority {(int)value}";

    private static string AutoUpgradeActionLabel(PlacementAutoUpgradeAction value) => value switch
    {
        PlacementAutoUpgradeAction.NoChange => "No change",
        PlacementAutoUpgradeAction.Disable => "Disable",
        _ => $"Priority {(int)value - 1}",
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void ActionKind_OnChecked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_ready || sender is not System.Windows.Controls.RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, out PlacementStepKind kind))
        {
            return;
        }
        _kind = kind;
        ConfigureFields();
    }

    private System.Windows.Controls.RadioButton SelectedActionButton(PlacementStepKind kind) => kind switch
    {
        PlacementStepKind.Reconfigure => ReconfigureActionButton,
        PlacementStepKind.Upgrade => UpgradeActionButton,
        PlacementStepKind.Sell => SellActionButton,
        _ => DelayActionButton,
    };

    private void Cancel_OnClick(object sender, RoutedEventArgs eventArgs) => DialogResult = false;

    private void Dialog_OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape) return;
        DialogResult = false;
        eventArgs.Handled = true;
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left) DragMove();
    }

}
