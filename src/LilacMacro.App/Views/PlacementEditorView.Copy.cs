using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Notifications;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlacementEditorView
{
    private void CopySetup_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (IsTestRunning || !_session.CanEdit) return;
        CopySourceMapSelector.ItemsSource = _availableMaps.OrderBy(item => item.CopyLabel).ToArray();
        CopySourceMapSelector.SelectedItem = _availableMaps.FirstOrDefault(item => !ReferenceEquals(item, _map)) ?? _map;
        RefreshCopySourceRoutes();
        CopySetupOverlay.Visibility = Visibility.Visible;
    }

    private void CopySourceMapSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        RefreshCopySourceRoutes();

    private void RefreshCopySourceRoutes()
    {
        if (CopySourceMapSelector.SelectedItem is not PlacementMapCardViewModel source) return;
        CopySourceRouteSelector.ItemsSource = PlacementRouteCatalog.For(source.Reference.Definition);
        CopySourceRouteSelector.SelectedIndex = 0;
    }

    private void CloseCopySetup_OnClick(object sender, RoutedEventArgs eventArgs) =>
        CopySetupOverlay.Visibility = Visibility.Collapsed;

    private async void ApplyCopySetup_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (IsTestRunning || !_session.CanEdit || _map is null ||
            CopySourceMapSelector.SelectedItem is not PlacementMapCardViewModel sourceMap ||
            CopySourceRouteSelector.SelectedItem is not PlacementRouteDefinition sourceRoute)
        {
            return;
        }

        CopySetupOverlay.IsEnabled = false;
        try
        {
            await _session.CopyFromAsync(
                sourceMap.Reference.Definition,
                sourceRoute,
                _map.ImageWidth,
                _map.ImageHeight);
            CopySetupOverlay.Visibility = Visibility.Collapsed;
            RefreshRoutes();
            RefreshWorkspace();
            AppToastService.ShowSuccess("SETUP COPIED", $"Copied {sourceMap.CopyLabel} / {sourceRoute.Label}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or InvalidOperationException or
                                           System.Text.Json.JsonException)
        {
            AppToastService.ShowError("COPY FAILED", exception.Message);
        }
        finally
        {
            CopySetupOverlay.IsEnabled = true;
        }
    }
}
