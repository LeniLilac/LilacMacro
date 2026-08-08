using System.Windows;
using System.Windows.Controls;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Vision;

namespace LilacMacro.App.Views;

public partial class ReviewVisualMatchPanel : UserControl
{
    private DatasetLocation? _dataset;
    private DatasetFrame? _frame;
    private OcrTrial? _trial;
    private bool _busy;

    public ReviewVisualMatchPanel()
    {
        InitializeComponent();
    }

    public event EventHandler<ReviewVisualMatchOverlay>? MatchCompleted;

    public void SetContext(DatasetLocation? dataset, DatasetFrame? frame, OcrTrial? trial)
    {
        _dataset = dataset;
        _frame = frame;
        _trial = trial;
        AnchorCombo.SelectedIndex = -1;
        AnchorCombo.ItemsSource = null;
        AnchorCombo.ItemsSource = trial?.Regions
            .Where(region => region.IsVisualAnchor)
            .Select(region => new ReviewVisualAnchorChoice(region))
            .ToArray() ?? [];
        AnchorCombo.SelectedIndex = AnchorCombo.Items.Count > 0 ? 0 : -1;
        Clear();
    }

    private async void Run_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_dataset is null || _frame is null || _trial is null ||
            AnchorCombo.SelectedItem is not ReviewVisualAnchorChoice choice || _busy) return;
        try
        {
            _busy = true;
            RunButton.IsEnabled = false;
            StatusText.Text = "BUILDING…";
            ReviewVisualMatchResult result = await Task.Run(() =>
                ReviewVisualMatchService.Run(_dataset, _frame, _trial, choice.Region));
            StatusText.Text = result.Summary;
            ScoresText.Text = result.Scores;
            ProfileText.Text = result.Profile;
            TimingsText.Text = result.Timings;
            CoordinatesText.Text = result.Coordinates;
            MedianImage.Source = result.MedianImage;
            ReliabilityImage.Source = result.ReliabilityImage;
            MatchedImage.Source = result.MatchedImage;
            ResultPanel.Visibility = Visibility.Visible;
            MatchCompleted?.Invoke(this, new ReviewVisualMatchOverlay(result.Bounds, result.Status == VisualAnchorMatchStatus.Matched));
        }
        catch (Exception error)
        {
            Clear(error.Message);
        }
        finally
        {
            _busy = false;
            RunButton.IsEnabled = AnchorCombo.SelectedItem is ReviewVisualAnchorChoice;
        }
    }

    private void Anchor_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (IsLoaded) Clear();
    }

    private void Clear(string status = "NO RESULT")
    {
        ResultPanel.Visibility = Visibility.Collapsed;
        bool hasAnchor = AnchorCombo.SelectedItem is ReviewVisualAnchorChoice;
        StatusText.Text = hasAnchor ? status : "MARK AN OCR RESULT IMAGE";
        RunButton.IsEnabled = hasAnchor && !_busy;
        MatchCompleted?.Invoke(this, new ReviewVisualMatchOverlay(null, false));
    }
}

internal sealed class ReviewVisualAnchorChoice(OcrTextRegion region)
{
    public OcrTextRegion Region { get; } = region;

    public string Text => Region.Text;
}

public sealed record ReviewVisualMatchOverlay(PixelRect? Bounds, bool Succeeded);
