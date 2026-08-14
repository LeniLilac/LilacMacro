using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

public partial class UtilityTaskTestPage : UserControl, IStoppableWorkspacePage
{
    private static readonly string[] Routes =
    [
        UtilityTaskPolicy.CalendarClaimRoute,
        ShopPurchasePolicy.GoldRoute,
        ShopPurchasePolicy.RaidRoute,
    ];

    private readonly UtilityTaskService _utilities;
    private readonly DeepDebugSessionService _deepDebug;
    private readonly ObservableCollection<ShopItemChoice> _items = [];
    private readonly string _device;
    private CancellationTokenSource? _cancellation;
    private int? _areasMenuVirtualKey = 'A';
    private int _reservedVirtualKey = 0x76;

    internal UtilityTaskTestPage(
        WorkspaceController workspace,
        OcrRunner ocr,
        DeepDebugSessionService deepDebug,
        string device)
    {
        _utilities = new UtilityTaskService(workspace, ocr);
        _deepDebug = deepDebug;
        _device = device;
        InitializeComponent();
        TaskBox.ItemsSource = Routes;
        TaskBox.SelectedIndex = 0;
        ShopItemsList.ItemsSource = _items;
        RefreshItems();
    }

    public bool IsRunning => _cancellation is not null;

    public async Task RefreshAsync()
    {
        MacroSettings settings = await new MacroSettingsStore().LoadAsync().ConfigureAwait(true);
        MacroKeyBindings bindings = new();
        bindings.ApplyPersisted(settings.KeyBindings);
        MacroRuntimeKeySnapshot keys = bindings.Snapshot();
        _areasMenuVirtualKey = keys.AreasMenu;
        _reservedVirtualKey = keys.MacroToggle;
        UpdateControls();
    }

    public async Task StopAsync()
    {
        if (_cancellation is null) return;
        _cancellation.Cancel();
        while (_cancellation is not null) await Task.Delay(20);
    }

    private async void RunButton_OnClick(object sender, RoutedEventArgs eventArgs) => await RunAsync();

    private void StopButton_OnClick(object sender, RoutedEventArgs eventArgs) => _cancellation?.Cancel();

    private void TaskBox_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) => RefreshItems();

    private async Task RunAsync()
    {
        if (IsRunning) return;
        string route = SelectedRoute();
        string[] selected = _items.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        try
        {
            UtilityTaskPolicy.Validate(route, selected);
            if (UtilityTaskPolicy.RequiresAreasMenu(route) && _areasMenuVirtualKey is null)
                throw new InvalidOperationException("The persisted Areas menu key is not configured.");

            RunLog.Items.Clear();
            _cancellation = new CancellationTokenSource();
            UpdateControls();
            SetStatus("RUNNING", "YellowBrush");
            IProgress<string> progress = new Progress<string>(ShowStatus);
            await _deepDebug.RunOperationAsync(
                OperationName(route),
                new DeepDebugOperationContext(
                    "runtime-lab",
                    new { Route = route, Items = selected, Device = _device }),
                async token =>
                {
                    await _utilities.RunAsync(
                        route,
                        selected,
                        _areasMenuVirtualKey,
                        _reservedVirtualKey,
                        _device,
                        message => progress.Report(message),
                        token).ConfigureAwait(false);
                    return true;
                },
                _cancellation.Token);
            SetStatus("COMPLETE", "SuccessBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("STOPPED", "YellowBrush");
        }
        catch (Exception error)
        {
            SetStatus("ERROR", "DangerBrush");
            ShowStatus($"ERROR | {error.Message}");
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            UpdateControls();
        }
    }

    private void RefreshItems()
    {
        if (!IsInitialized) return;
        _items.Clear();
        string route = SelectedRoute();
        if (ShopPurchasePolicy.IsShopRoute(route))
        {
            foreach (ShopItemDefinition item in ShopPurchasePolicy.ItemsFor(route))
                _items.Add(new ShopItemChoice(item.Id, item.DisplayName));
        }
        ShopItemsPanel.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStatus(string message)
    {
        RunLog.Items.Add($"{DateTime.Now:HH:mm:ss} {message}");
        RunLog.ScrollIntoView(RunLog.Items[^1]);
    }

    private void UpdateControls()
    {
        RunButton.IsEnabled = !IsRunning;
        StopButton.IsEnabled = IsRunning;
        TaskBox.IsEnabled = !IsRunning;
        ShopItemsList.IsEnabled = !IsRunning;
    }

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusBand.SetResourceReference(Border.BackgroundProperty, brush);
    }

    private string SelectedRoute() => TaskBox.SelectedItem as string ?? Routes[0];

    private static string OperationName(string route) => route switch
    {
        UtilityTaskPolicy.CalendarClaimRoute => "calendar-claim-test",
        ShopPurchasePolicy.GoldRoute => "gold-shop-test",
        ShopPurchasePolicy.RaidRoute => "raid-shop-test",
        _ => "utility-task-test",
    };

    private sealed class ShopItemChoice(string id, string displayName)
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public bool IsSelected { get; set; } = true;
    }
}
