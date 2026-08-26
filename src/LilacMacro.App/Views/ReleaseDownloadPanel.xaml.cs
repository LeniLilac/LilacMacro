using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.App.Updates;
using LilacMacro.Core.Updates;

namespace LilacMacro.App.Views;

internal sealed record ReleaseDownloadOption(VerifiedUpdateRelease Release)
{
    public string Label => Release.Prerelease
        ? $"{Release.Version} · PRERELEASE"
        : Release.Version.ToString();
}

public partial class ReleaseDownloadPanel : UserControl
{
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private ApplicationUpdateService? updates;
    private MacroOwnerState? ownerState;
    private CancellationTokenSource? operationCancellation;
    private bool? loadedPrerelease;

    public ReleaseDownloadPanel() => InitializeComponent();

    internal void Initialize(ApplicationUpdateService updateService, MacroOwnerState state)
    {
        updates = updateService;
        ownerState = state;
        RefreshOwnership();
    }

    internal void RefreshOwnership()
    {
        bool owner = !MacroInstanceContext.Current.IsManagedRunner;
        bool enabled = owner && ownerState?.OnlineFeaturesEnabled == true;
        VersionCombo.IsEnabled = enabled;
        DownloadButton.IsEnabled = enabled && VersionCombo.SelectedItem is ReleaseDownloadOption;
        if (!owner) StatusText.Text = "Download releases from This desktop.";
        else if (!enabled) StatusText.Text = "Online features are disabled.";
        else if (VersionCombo.Items.Count == 0) StatusText.Text = "Open the list to load official releases.";
    }

    internal void ResetCatalog()
    {
        loadedPrerelease = null;
        VersionCombo.ItemsSource = null;
        DownloadButton.IsEnabled = false;
        RefreshOwnership();
    }

    private async void VersionCombo_OnDropDownOpened(object sender, EventArgs eventArgs)
    {
        if (updates is null || ownerState is null
            || loadedPrerelease == ownerState.IncludePrereleaseUpdates
            || !await loadGate.WaitAsync(0)) return;
        try
        {
            if (!await ownerState.IsOnlineFeaturesDurablyEnabledAsync())
            {
                StatusText.Text = "Online features are disabled.";
                return;
            }
            StartOperation();
            StatusText.Text = "Loading official releases...";
            bool includePrerelease = ownerState.IncludePrereleaseUpdates;
            IReadOnlyList<VerifiedUpdateRelease> releases = await updates.ListReleasesAsync(
                includePrerelease,
                operationCancellation!.Token);
            if (ownerState.IncludePrereleaseUpdates != includePrerelease)
            {
                ResetCatalog();
                return;
            }
            VersionCombo.ItemsSource = releases.Select(release => new ReleaseDownloadOption(release)).ToArray();
            loadedPrerelease = includePrerelease;
            StatusText.Text = releases.Count == 0
                ? "No downloadable releases found."
                : $"{releases.Count} official {(releases.Count == 1 ? "release" : "releases")} available.";
        }
        catch (OperationCanceledException) when (operationCancellation?.IsCancellationRequested == true) { }
        catch (Exception exception) when (IsExpectedDownloadError(exception))
        {
            StatusText.Text = $"Release list failed: {exception.Message}";
        }
        finally { loadGate.Release(); }
    }

    private void VersionCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        DownloadButton.IsEnabled = VersionCombo.IsEnabled && VersionCombo.SelectedItem is ReleaseDownloadOption;

    private async void DownloadButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (updates is null || ownerState is null
            || VersionCombo.SelectedItem is not ReleaseDownloadOption option
            || !await ownerState.IsOnlineFeaturesDurablyEnabledAsync()) return;
        SaveFileDialog dialog = new()
        {
            FileName = $"LilacMacro-Setup-{option.Release.Version}.exe",
            DefaultExt = ".exe",
            Filter = "Windows installer (*.exe)|*.exe",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        StartOperation();
        VersionCombo.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        StatusText.Text = $"Downloading and verifying {option.Release.Version}...";
        try
        {
            await updates.DownloadReleaseInstallerAsync(
                option.Release,
                dialog.FileName,
                operationCancellation!.Token);
            StatusText.Text = $"Saved {Path.GetFileName(dialog.FileName)}";
            AppToastService.ShowSuccess("INSTALLER DOWNLOADED", Path.GetFileName(dialog.FileName));
        }
        catch (OperationCanceledException) when (operationCancellation?.IsCancellationRequested == true) { }
        catch (Exception exception) when (IsExpectedDownloadError(exception))
        {
            StatusText.Text = $"Download failed: {exception.Message}";
            AppToastService.ShowError("DOWNLOAD FAILED", exception.Message);
        }
        finally { RefreshOwnership(); }
    }

    private void StartOperation()
    {
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource();
    }

    private void ReleaseDownloadPanel_OnUnloaded(object sender, RoutedEventArgs eventArgs) =>
        operationCancellation?.Cancel();

    private static bool IsExpectedDownloadError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or HttpRequestException
            or InvalidDataException or InvalidOperationException or TaskCanceledException;
}
