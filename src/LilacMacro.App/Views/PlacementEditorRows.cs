using System.ComponentModel;
using System.Runtime.CompilerServices;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public sealed record PlacementRouteRowViewModel(
    PlacementRouteDefinition Definition,
    string State)
{
    public string Id => Definition.Id;

    public string Label => Definition.Label;

    public string Display => $"{Label}  /  {State}";
}

public sealed record PlacementReferenceOption(Guid Id, string Label);

public sealed record PlacementNumberOption(int Value, string Label);

public sealed record PlacementOption<T>(T Value, string Label) where T : struct, Enum;

public static class PlacementStepRowFactory
{
    public static IReadOnlyList<PlacementStepRowViewModel> Create(
        PlacementRouteSetup route,
        int surfaceWidth = 1366,
        int surfaceHeight = 700,
        PlacementCursorMode cursorMode = PlacementCursorMode.Place)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(surfaceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(surfaceHeight);
        IReadOnlyDictionary<Guid, string> placementLabels =
            PlacementReferencePolicy.BuildDisplayLabels(route.Steps);
        int startGameIndex = route.Steps.FindIndex(step => step.Kind == PlacementStepKind.StartGame);
        return route.Steps.Select((step, index) =>
            new PlacementStepRowViewModel(
                step,
                index,
                startGameIndex,
                placementLabels,
                step.Kind == PlacementStepKind.Place
                    ? PlacementMarkerPresentation.Create(step.X, step.Y)
                    : PlacementMarkerPresentation.Empty,
                cursorMode)).ToArray();
    }
}

public sealed class PlacementStepRowViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<Guid, string> _placementLabels;
    private bool _isNearPointer;

    public PlacementStepRowViewModel(
        PlacementStep step,
        int index,
        int startGameIndex,
        IReadOnlyDictionary<Guid, string> placementLabels,
        PlacementMarkerPresentation markerLayout,
        PlacementCursorMode cursorMode)
    {
        Step = step;
        Index = index;
        StartGameIndex = startGameIndex;
        _placementLabels = placementLabels;
        MarkerLayout = markerLayout;
        CursorMode = cursorMode;
    }

    public PlacementStep Step { get; }

    public int Index { get; }

    public int StartGameIndex { get; }

    public bool IsPlacement => Step.Kind == PlacementStepKind.Place;

    public bool CanDelete => Step.Kind != PlacementStepKind.StartGame;

    public bool CanEdit => Step.Kind != PlacementStepKind.StartGame;

    public string MarkerLabel => _placementLabels.GetValueOrDefault(Step.Id, Step.UnitSlot.ToString());

    public PlacementMarkerPresentation MarkerLayout { get; }

    public PlacementCursorMode CursorMode { get; }

    public bool IsSelectionMode => CursorMode == PlacementCursorMode.Select;

    public bool IsNearPointer
    {
        get => _isNearPointer;
        private set
        {
            if (_isNearPointer == value) return;
            _isNearPointer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinOpacity));
        }
    }

    public double PinOpacity => !IsSelectionMode && IsNearPointer ? 0.18 : 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetNearPointer(bool value) => IsNearPointer = value;

    public string Phase => Step.Kind == PlacementStepKind.StartGame
        ? "START"
        : Index < StartGameIndex ? "BEFORE" : "AFTER";

    public string Title => Step.Kind switch
    {
        PlacementStepKind.Place => $"{MarkerLabel}  PLACE UNIT {Step.UnitSlot}",
        PlacementStepKind.Reconfigure => $"RECONFIGURE {TargetLabel}",
        PlacementStepKind.Delay => "DELAY",
        PlacementStepKind.Upgrade => $"UPGRADE {TargetLabel}",
        PlacementStepKind.StartGame => "START GAME",
        PlacementStepKind.Sell => $"SELL {TargetLabel}",
        _ => Step.Kind.ToString().ToUpperInvariant(),
    };

    public string Detail => Step.Kind switch
    {
        PlacementStepKind.Place => $"{Step.X}, {Step.Y}  ·  {Step.TargetingPriority}  ·  {AutoUpgradeLabel(Step.AutoUpgradePriority)}",
        PlacementStepKind.Reconfigure => ReconfigureDetail(),
        PlacementStepKind.Delay => $"{Step.DelayDurationMilliseconds} MS",
        PlacementStepKind.Upgrade => $"{Step.UpgradeCount} PRESSES",
        PlacementStepKind.StartGame => "TIMELINE BOUNDARY",
        PlacementStepKind.Sell => $"{Step.DelayAfterMilliseconds} MS AFTER",
        _ => string.Empty,
    };

    private string TargetLabel => Step.TargetPlacementId is Guid target &&
        _placementLabels.TryGetValue(target, out string? label)
            ? label
            : "PLACEMENT";

    private string ReconfigureDetail()
    {
        List<string> changes = [];
        if (Step.ChangeTargetingPriority) changes.Add(Step.TargetingPriority.ToString().ToUpperInvariant());
        if (Step.AutoUpgradeAction != PlacementAutoUpgradeAction.NoChange)
        {
            changes.Add(Step.AutoUpgradeAction.ToString().ToUpperInvariant());
        }
        return changes.Count == 0 ? "NO CHANGES" : string.Join("  ·  ", changes);
    }

    private static string AutoUpgradeLabel(PlacementAutoUpgradePriority value) => value == PlacementAutoUpgradePriority.Off
        ? "AUTO OFF"
        : $"AUTO {(int)value}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
