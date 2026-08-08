using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Datasets;

namespace LilacMacro.App.Views;

public partial class DatasetsPage : UserControl, IWorkspacePage
{
    private readonly WorkspaceController _workspace;
    private readonly Func<PageKind, Task> _navigate;

    public DatasetsPage(WorkspaceController workspace, Func<PageKind, Task> navigate)
    {
        InitializeComponent();
        _workspace = workspace;
        _navigate = navigate;
    }

    public async Task RefreshAsync()
    {
        RootText.Text = _workspace.DatasetRoot;
        IReadOnlyList<DatasetLocation> datasets = await _workspace.DiscoverDatasetsAsync();
        DatasetList.ItemsSource = datasets.Select(dataset => new DatasetListItem(
            dataset,
            DisplayName(dataset),
            $"{dataset.Manifest.Frames.Count} frames  ·  {dataset.Manifest.ClientWidth} × {dataset.Manifest.ClientHeight}  ·  {dataset.Manifest.CreatedAtUtc.LocalDateTime:g}",
            dataset.Manifest.IsFinalized ? "FINAL" : "DRAFT")).ToArray();
        EmptyState.Visibility = datasets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DatasetList.Visibility = datasets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (datasets.Count > 0) DatasetList.SelectedIndex = 0;
    }

    private void DatasetList_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        DatasetListItem? selected = DatasetList.SelectedItem as DatasetListItem;
        SelectionName.Text = selected?.Name ?? "Choose a dataset";
        SelectionPath.Text = selected?.Dataset.DirectoryPath ?? "—";
        OpenButton.IsEnabled = selected is not null;
        ExplorerButton.IsEnabled = selected is not null;
    }

    private async void Open_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (DatasetList.SelectedItem is not DatasetListItem selected) return;
        try
        {
            await _workspace.OpenDatasetAsync(selected.Dataset.DirectoryPath);
            await _navigate(PageKind.Review);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Open dataset", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Explorer_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (DatasetList.SelectedItem is not DatasetListItem selected) return;
        Process.Start(new ProcessStartInfo("explorer.exe", selected.Dataset.DirectoryPath) { UseShellExecute = true });
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs eventArgs) => await RefreshAsync();

    private static string DisplayName(DatasetLocation dataset) => string.IsNullOrWhiteSpace(dataset.Manifest.Name)
        ? $"Unnamed draft · {dataset.Manifest.CreatedAtUtc.LocalDateTime:g}"
        : dataset.Manifest.Name;

    private sealed record DatasetListItem(
        DatasetLocation Dataset,
        string Name,
        string Detail,
        string State);
}
