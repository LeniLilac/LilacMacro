using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlanSharePanel : UserControl
{
    private readonly MacroOwnerState _ownerState;
    private readonly PlanPrototype _selectedPlan;
    private readonly PlanShareClient _client = new();
    private readonly ConfigurationMutationGate _configurationGate = ConfigurationMutationGate.CreateDefault();
    private readonly PlacementSetupStore _placements = new(Path.Combine(
        MacroInstanceContext.Current.ConfigurationRoot,
        "placements"));
    private bool _isBusy;

    internal PlanSharePanel(MacroOwnerState ownerState, PlanPrototype selectedPlan)
    {
        _ownerState = ownerState;
        _selectedPlan = selectedPlan;
        InitializeComponent();
    }

    internal event EventHandler? CloseRequested;

    private async void Export_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        bool includePlan = ExportPlanCheck.IsChecked == true;
        bool includePlacements = ExportPlacementsCheck.IsChecked == true;
        if (!includePlan && !includePlacements)
        {
            SetStatus("Select at least one item to export.");
            return;
        }
        await RunAsync(async () =>
        {
            await RequireOnlineFeaturesAsync();
            await _ownerState.FlushAsync();
            PlanShareBundle bundle = new()
            {
                Plan = includePlan
                    ? PlanPersistence.CreateSnapshot([_selectedPlan]).Single()
                    : null,
                Placements = includePlacements
                    ? await LoadPlacementsAsync()
                    : [],
            };
            CreatedPlanShare created = await _client.CreateAsync(PlanShareBundleCodec.Encode(bundle));
            ExportCodeText.Text = PlanShareClient.FormatCode(created.Code);
            SetStatus($"Code ready. It expires {created.ExpiresAt.LocalDateTime:g}.");
        });
    }

    private async void Import_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        bool includePlan = ImportPlanCheck.IsChecked == true;
        bool includePlacements = ImportPlacementsCheck.IsChecked == true;
        if (!includePlan && !includePlacements)
        {
            SetStatus("Select at least one item to import.");
            return;
        }
        await RunAsync(async () =>
        {
            await RequireOnlineFeaturesAsync();
            FetchedPlanShare fetched = await _client.GetAsync(ImportCodeText.Text);
            PlanShareBundle bundle = await Task.Run(() => PlanShareBundleCodec.Decode(fetched.Payload));
            PlanPrototype? importedPlan = includePlan && bundle.Plan is not null
                ? RestorePlan(bundle.Plan)
                : null;
            PlacementSetupDocument[] importedPlacements = includePlacements
                ? bundle.Placements.ToArray()
                : [];
            if (importedPlan is null && importedPlacements.Length == 0)
                throw new InvalidDataException("The share does not contain the selected item types.");

            using IDisposable mutationLease = _configurationGate.AcquireMutationLease();
            using PlacementSetupBatch placementBatch = await _placements.BeginBatchAsync(importedPlacements);
            PlanPrototype previousPlan = _ownerState.SelectedPlan;
            bool planAdded = false;
            try
            {
                if (importedPlan is not null)
                {
                    importedPlan.Name = UniquePlanName(importedPlan.Name);
                    _ownerState.Plans.Add(importedPlan);
                    _ownerState.SelectPlan(importedPlan);
                    planAdded = true;
                    _ownerState.NotifyPlansChanged();
                    await _ownerState.FlushAsync();
                }
                placementBatch.Commit();
            }
            catch
            {
                if (planAdded)
                {
                    _ownerState.Plans.Remove(importedPlan!);
                    _ownerState.SelectPlan(previousPlan);
                    _ownerState.NotifyPlansChanged();
                    try { await _ownerState.FlushAsync(); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
                throw;
            }
            SetStatus($"Imported {(importedPlan is null ? 0 : 1)} plan and {importedPlacements.Length} map setup(s).");
        });
    }

    private async Task<List<PlacementSetupDocument>> LoadPlacementsAsync()
    {
        List<PlacementSetupDocument> result = [];
        foreach (PlacementMapDefinition map in PlacementMapCatalog.Definitions)
        {
            try { result.Add(await _placements.LoadAsync(map.Id)); }
            catch (FileNotFoundException) { }
        }
        return result;
    }

    private static PlanPrototype RestorePlan(PlanSettingsSnapshot snapshot)
    {
        if (!PlanPersistence.TryRestore([snapshot], out ObservableCollection<PlanPrototype>? plans))
            throw new InvalidDataException("The shared plan is invalid.");
        return plans.Single();
    }

    private string UniquePlanName(string requested)
    {
        HashSet<string> names = _ownerState.Plans.Select(plan => plan.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(requested)) return requested;
        for (int suffix = 2; suffix <= 999; suffix++)
        {
            string candidate = $"{requested} ({suffix})";
            if (candidate.Length <= 100 && !names.Contains(candidate)) return candidate;
        }
        throw new InvalidDataException("A unique imported plan name could not be created.");
    }

    private async Task RequireOnlineFeaturesAsync()
    {
        if (!await _ownerState.IsOnlineFeaturesDurablyEnabledAsync())
            throw new InvalidOperationException("Enable Online features in Settings before sharing configurations.");
    }

    private async Task RunAsync(Func<Task> operation)
    {
        SetBusy(true);
        SetStatus("WORKING...");
        try { await operation(); }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException or
                                           IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            SetStatus(exception is TaskCanceledException ? "The sharing request timed out." : exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyCode_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(ExportCodeText.Text)) return;
        try { Clipboard.SetText(ExportCodeText.Text); }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("Windows clipboard is busy. Try Copy again.");
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isBusy)
        {
            SetStatus("Wait for the current sharing request to finish.");
            return;
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        ExportButton.IsEnabled = !value;
        ImportButton.IsEnabled = !value;
        ExportPlanCheck.IsEnabled = !value;
        ExportPlacementsCheck.IsEnabled = !value;
        ImportCodeText.IsEnabled = !value;
        ImportPlanCheck.IsEnabled = !value;
        ImportPlacementsCheck.IsEnabled = !value;
        CopyCodeButton.IsEnabled = !value;
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
