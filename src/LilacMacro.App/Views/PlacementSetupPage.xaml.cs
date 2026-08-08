using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Notifications;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class PlacementSetupPage : UserControl
{
    private readonly PlacementMapCatalog _catalog = new();
    private readonly string _datasetRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "LilacMacro Datasets");
    private IReadOnlyList<PlacementMapCardViewModel> _maps = [];
    private PlacementMapMode _selectedMode = PlacementMapMode.Story;
    private bool _loaded;
    private double _galleryOffset;

    public PlacementSetupPage()
    {
        InitializeComponent();
        Loaded += PlacementSetupPage_OnLoaded;
        ApplyCategoryStyles();
    }

    public Task FlushAsync() => PlacementEditor.FlushAsync();

    private async void PlacementSetupPage_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_loaded) return;
        _loaded = true;
        await LoadMapsAsync();
    }

    private async Task LoadMapsAsync()
    {
        RefreshButton.IsEnabled = false;
        GalleryStateText.Text = "LOADING MAPS";
        GalleryStateText.Visibility = Visibility.Visible;
        try
        {
            IReadOnlyList<PlacementMapReference> references = await _catalog.DiscoverAsync(_datasetRoot);
            _maps = references.Select(reference => new PlacementMapCardViewModel(reference)).ToArray();
            ShowSelectedMode();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _maps = [];
            MapGallery.ItemsSource = null;
            GalleryStateText.Text = "MAPS UNAVAILABLE";
            AppToastService.ShowError("MAPS UNAVAILABLE", exception.Message);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void SelectMode(PlacementMapMode mode)
    {
        _selectedMode = mode;
        ApplyCategoryStyles();
        ShowSelectedMode();
    }

    private void ShowSelectedMode()
    {
        PlacementMapCardViewModel[] visibleMaps = _maps.Where(map => map.Mode == _selectedMode).ToArray();
        MapGallery.ItemsSource = visibleMaps;
        GalleryStateText.Text = visibleMaps.Length == 0 ? "NO MAPS" : string.Empty;
        GalleryStateText.Visibility = visibleMaps.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyCategoryStyles()
    {
        StoryButton.Style = CategoryStyle(PlacementMapMode.Story);
        RaidButton.Style = CategoryStyle(PlacementMapMode.Raid);
        ExpeditionButton.Style = CategoryStyle(PlacementMapMode.Expedition);
        EventsButton.Style = CategoryStyle(PlacementMapMode.Events);
    }

    private Style CategoryStyle(PlacementMapMode mode) =>
        (Style)FindResource(mode == _selectedMode ? "CategoryButtonActiveStyle" : "CategoryButtonStyle");

    private async void MapTile_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: PlacementMapCardViewModel map }) return;
        _galleryOffset = GalleryScroll.VerticalOffset;
        GalleryPanel.Visibility = Visibility.Collapsed;
        PlacementEditor.Visibility = Visibility.Visible;
        await PlacementEditor.OpenAsync(map);
    }

    private void PlacementEditor_OnBackRequested(object? sender, EventArgs eventArgs)
    {
        PlacementEditor.Visibility = Visibility.Collapsed;
        GalleryPanel.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => GalleryScroll.ScrollToVerticalOffset(_galleryOffset));
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs eventArgs) => await LoadMapsAsync();

    private void StoryButton_OnClick(object sender, RoutedEventArgs eventArgs) => SelectMode(PlacementMapMode.Story);

    private void RaidButton_OnClick(object sender, RoutedEventArgs eventArgs) => SelectMode(PlacementMapMode.Raid);

    private void ExpeditionButton_OnClick(object sender, RoutedEventArgs eventArgs) => SelectMode(PlacementMapMode.Expedition);

    private void EventsButton_OnClick(object sender, RoutedEventArgs eventArgs) => SelectMode(PlacementMapMode.Events);
}
