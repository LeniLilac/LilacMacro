using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.App.Views;

public partial class LocalInstanceManagerPanel : UserControl
{
    private readonly ObservableCollection<LocalInstanceRow> _rows = [];
    private LocalInstanceManagerController? _manager;
    private MacroOwnerState? _ownerState;
    private bool _busy;

    public LocalInstanceManagerPanel()
    {
        InitializeComponent();
        InstanceItems.ItemsSource = _rows;
        DesktopConfigurationText.Text = MacroInstanceContext.Current.UsesMachineProtectedSecrets ? "SHARED" : "LOCAL";
    }

    internal void Initialize(LocalInstanceManagerController manager, MacroOwnerState ownerState)
    {
        _manager = manager;
        _ownerState = ownerState;
        _ = RefreshAsync();
    }

    private async void Setup_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await MutateAsync(manager => manager.InstallAsync());

    private async void Repair_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await MutateAsync(manager => manager.RepairAsync());

    private async void AddShared_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await MutateAsync(manager => manager.AddAsync(RunnerConfigurationMode.Shared));

    private async void AddIsolated_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await MutateAsync(manager => manager.AddAsync(RunnerConfigurationMode.Isolated));

    private async void RemoveAll_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await MutateAsync(manager => manager.RemoveAllAsync());

    private async void RemoveProfile_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string profileId })
            await MutateAsync(manager => manager.RemoveAsync(profileId));
    }

    private async void Open_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_manager is null || _ownerState is null || sender is not Button { Tag: string profileId }) return;
        try { await _manager.OpenAsync(profileId, _ownerState.RunnerLayoutProfile(profileId)); }
        catch (Exception exception) when (IsExpected(exception))
        {
            AppToastService.ShowError("LOCAL INSTANCE FAILED", exception.Message);
        }
        await RefreshAsync();
    }

    private async void Viewport_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_ownerState is null
            || sender is not ComboBox { Tag: LocalInstanceRow row, SelectedItem: LocalInstanceViewportOption option }
            || row.SelectedViewport.Profile == option.Profile)
        {
            return;
        }
        row.SelectedViewport = option;
        _ownerState.SetRunnerLayoutProfile(row.Id, option.Profile);
        try
        {
            await _ownerState.FlushAsync();
            ManagerDetailText.Text = $"{row.DisplayName} will open at {option.Label}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppToastService.ShowError("LOCAL INSTANCE SAVE FAILED", exception.Message);
        }
    }

    private async Task MutateAsync(Func<LocalInstanceManagerController, Task<LocalInstanceManagerSnapshot>> mutation)
    {
        if (_manager is null || _ownerState is null || _busy) return;
        _busy = true;
        SetActionsEnabled(false);
        try
        {
            await _ownerState.FlushAsync();
            LocalInstanceManagerSnapshot snapshot = await mutation(_manager);
            await MacroConfigurationMigrator.EnsureOwnerSharedConfigurationAsync();
            Apply(snapshot);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            AppToastService.ShowError("LOCAL INSTANCE FAILED", exception.Message);
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_manager is null) return;
        try { Apply(await _manager.GetSnapshotAsync()); }
        catch (Exception exception) when (IsExpected(exception))
        {
            ManagerStateText.Text = "UNAVAILABLE";
            ManagerDetailText.Text = exception.Message;
        }
    }

    private void Apply(LocalInstanceManagerSnapshot snapshot)
    {
        _rows.Clear();
        foreach (LocalInstanceProfileStatus item in snapshot.Profiles)
        {
            MacroLayoutProfile layout = _ownerState?.RunnerLayoutProfile(item.Profile.Id)
                ?? MacroLayoutProfile.Full1920x1080;
            _rows.Add(new LocalInstanceRow(
                item.Profile.Id,
                item.Profile.DisplayName,
                item.Profile.AccountName,
                item.Session.State.ToString().ToUpperInvariant(),
                item.Profile.ConfigurationMode == RunnerConfigurationMode.Shared ? "SHARED" : "SEPARATE",
                $"{item.Profile.LoopbackAddress}:{TermServiceConfigurationManager.LocalPort}",
                ViewportOptions.Single(option => option.Profile == layout)));
        }
        ManagerStateText.Text = snapshot.Status.State.ToString().ToUpperInvariant();
        ManagerDetailText.Text = snapshot.Status.Problems.FirstOrDefault() ?? snapshot.Status.Detail;
        bool ownerUi = !MacroInstanceContext.Current.IsManagedRunner;
        bool installed = snapshot.Status.State is not LocalSessionState.Absent;
        InstanceItems.IsEnabled = ownerUi && !_busy;
        SetupButton.IsEnabled = ownerUi && !_busy && !installed;
        RepairButton.IsEnabled = ownerUi && !_busy && installed;
        AddSharedButton.IsEnabled = ownerUi && !_busy && installed && _rows.Count < 16;
        AddIsolatedButton.IsEnabled = ownerUi && !_busy && installed && _rows.Count < 16;
        RemoveAllButton.IsEnabled = ownerUi && !_busy && installed;
    }

    private void SetActionsEnabled(bool enabled)
    {
        SetupButton.IsEnabled = enabled;
        RepairButton.IsEnabled = enabled;
        AddSharedButton.IsEnabled = enabled;
        AddIsolatedButton.IsEnabled = enabled;
        RemoveAllButton.IsEnabled = enabled;
    }

    private static bool IsExpected(Exception exception) => exception is IOException
        or InvalidDataException
        or InvalidOperationException
        or UnauthorizedAccessException
        or System.ComponentModel.Win32Exception;

    private static IReadOnlyList<LocalInstanceViewportOption> ViewportOptions { get; } =
    [
        new("1920 x 1080", MacroLayoutProfile.Full1920x1080),
        new("1366 x 768", MacroLayoutProfile.Compact1366x768),
    ];

    private sealed record LocalInstanceViewportOption(string Label, MacroLayoutProfile Profile);

    private sealed class LocalInstanceRow(
        string id,
        string displayName,
        string accountName,
        string sessionState,
        string configurationMode,
        string endpoint,
        LocalInstanceViewportOption selectedViewport)
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public string AccountName { get; } = accountName;
        public string SessionState { get; } = sessionState;
        public string ConfigurationMode { get; } = configurationMode;
        public string Endpoint { get; } = endpoint;
        public IReadOnlyList<LocalInstanceViewportOption> ViewportOptions { get; } = LocalInstanceManagerPanel.ViewportOptions;
        public LocalInstanceViewportOption SelectedViewport { get; set; } = selectedViewport;
    }
}
