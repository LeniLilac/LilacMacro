using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlanShareWindow : Window
{
    private readonly MacroOwnerState _ownerState;
    private readonly PlanPrototype _selectedPlan;
    private readonly PlanShareClient _client = new();
    private readonly ConfigurationMutationGate _configurationGate = ConfigurationMutationGate.CreateDefault();
    private readonly PlacementSetupStore _placements = new(Path.Combine(
        MacroInstanceContext.Current.ConfigurationRoot,
        "placements"));

    internal PlanShareWindow(MacroOwnerState ownerState, PlanPrototype selectedPlan)
    {
        _ownerState = ownerState;
        _selectedPlan = selectedPlan;
        InitializeComponent();
    }

    private async void Export_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ExportPlanCheck.IsChecked != true && ExportPlacementsCheck.IsChecked != true)
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
                Plan = ExportPlanCheck.IsChecked == true
                    ? PlanPersistence.CreateSnapshot([_selectedPlan]).Single()
                    : null,
                Placements = ExportPlacementsCheck.IsChecked == true
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
        if (ImportPlanCheck.IsChecked != true && ImportPlacementsCheck.IsChecked != true)
        {
            SetStatus("Select at least one item to import.");
            return;
        }
        await RunAsync(async () =>
        {
            await RequireOnlineFeaturesAsync();
            FetchedPlanShare fetched = await _client.GetAsync(ImportCodeText.Text);
            PlanShareBundle bundle = await Task.Run(() => PlanShareBundleCodec.Decode(fetched.Payload));
            PlanPrototype? importedPlan = ImportPlanCheck.IsChecked == true && bundle.Plan is not null
                ? RestorePlan(bundle.Plan)
                : null;
            PlacementSetupDocument[] importedPlacements = ImportPlacementsCheck.IsChecked == true
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

    private PlanPrototype RestorePlan(PlanSettingsSnapshot snapshot)
    {
        if (!PlanPersistence.TryRestore([snapshot], out ObservableCollection<PlanPrototype>? plans))
            throw new InvalidDataException("The shared plan is invalid.");
        return plans.Single();
    }

    private string UniquePlanName(string requested)
    {
        HashSet<string> names = _ownerState.Plans.Select(plan => plan.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        ExportButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        SetStatus("WORKING...");
        try { await operation(); }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException or
                                           IOException or UnauthorizedAccessException or HttpRequestException or
                                           TaskCanceledException)
        {
            SetStatus(exception is TaskCanceledException ? "The sharing request timed out." : exception.Message);
        }
        finally
        {
            ExportButton.IsEnabled = true;
            ImportButton.IsEnabled = true;
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

    private void Close_OnClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void SetStatus(string message) => StatusText.Text = message;
}
