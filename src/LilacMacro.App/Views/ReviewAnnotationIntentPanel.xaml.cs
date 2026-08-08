using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Views;

public partial class ReviewAnnotationIntentPanel : UserControl
{
    private BoxAnnotation? _annotation;
    private OcrTrial? _trial;
    private IReadOnlyCollection<OcrTextRegion> _evidenceUniverse = [];
    private bool _binding;

    public ReviewAnnotationIntentPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? IntentChanged;

    public event EventHandler? FilterChanged;

    public bool HideUnchecked => HideUncheckedToggle.IsChecked == true;

    public bool SetContext(
        BoxAnnotation? annotation,
        OcrTrial? trial,
        IReadOnlyCollection<OcrTextRegion>? evidenceUniverse = null)
    {
        _annotation = annotation;
        _trial = trial;
        _evidenceUniverse = evidenceUniverse ?? trial?.Regions ?? [];
        int previousMinimum = annotation?.MinimumPoolMatches ?? 0;
        bool changed = ApplyAutomaticRules();
        BindCandidates();
        return changed || annotation?.MinimumPoolMatches != previousMinimum;
    }

    private void BindCandidates()
    {
        _binding = true;
        try
        {
            if (_annotation is null || _trial is null || _trial.Regions.Count == 0)
            {
                CandidatesList.ItemsSource = null;
                CandidatesPanel.Visibility = Visibility.Collapsed;
                return;
            }

            OcrTextRegion[] regions = _trial.Regions.ToArray();
            ReviewDetectedTextItem[] items = regions
                .Where(region => !HideUnchecked || region.IsOcrEvidence || region.IsVisualAnchor)
                .Select(region => new ReviewDetectedTextItem(region, regions, _annotation.Bounds))
                .ToArray();
            CandidatesList.ItemsSource = items;
            CandidatesPanel.Visibility = Visibility.Visible;
            BindRuleSummary(_evidenceUniverse);
        }
        finally
        {
            _binding = false;
        }
    }

    private void BindRuleSummary(IReadOnlyCollection<OcrTextRegion> regions)
    {
        if (_annotation is null) return;
        int required = OcrEvidenceRulePolicy.DistinctPhrases(regions, OcrEvidenceRole.Required).Count;
        int pool = OcrEvidenceRulePolicy.DistinctPhrases(regions, OcrEvidenceRole.Pool).Count;
        _annotation.MinimumPoolMatches = OcrEvidenceRulePolicy.ClampMinimumPoolMatches(
            _annotation.MinimumPoolMatches,
            regions);
        RuleSummaryText.Text = pool == 0
            ? $"{required} REQUIRED"
            : $"{required} REQUIRED  +  {_annotation.MinimumPoolMatches} OF {pool} POOL";
        PoolMinimumCombo.Visibility = pool == 0 ? Visibility.Collapsed : Visibility.Visible;
        PoolMinimumCombo.ItemsSource = Enumerable.Range(1, pool).ToArray();
        PoolMinimumCombo.SelectedItem = _annotation.MinimumPoolMatches;
    }

    private bool ApplyAutomaticRules()
    {
        if (_annotation is null || _trial is null) return false;
        bool changed = false;
        foreach (OcrTextRegion region in _trial.Regions.Where(IsRelevant))
        {
            if (region.SpatialSelectorOverridden) continue;
            OcrSpatialSelector inferred = OcrSpatialRuleInference.Infer(region, _trial.Regions, _annotation.Bounds);
            if (region.SpatialSelector == inferred) continue;
            region.SpatialSelector = inferred;
            region.SpatialAnchorText = null;
            changed = true;
        }
        return changed;
    }

    private void HideUnchecked_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        FilterChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(BindCandidates, DispatcherPriority.ContextIdle);
    }

    private void CandidateRole_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_binding || sender is not CheckBox { DataContext: ReviewDetectedTextItem item } toggle) return;
        if (toggle.Tag as string == "ocr")
        {
            item.Region.IsOcrEvidence = toggle.IsChecked == true;
            if (!item.Region.IsOcrEvidence) item.Region.MatchMode = OcrMatchMode.Exact;
        }
        else if (toggle.Tag as string == "image")
        {
            item.Region.IsVisualAnchor = toggle.IsChecked == true;
        }
        else
        {
            item.Region.MatchMode = toggle.IsChecked == true ? OcrMatchMode.FuzzyPhrase : OcrMatchMode.Exact;
        }

        if (!IsRelevant(item.Region)) item.Region.EvidenceRole = OcrEvidenceRole.None;
        CompleteEdit();
    }

    private void EvidenceRole_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_binding || sender is not ComboBox
            {
                DataContext: ReviewDetectedTextItem item,
                SelectedItem: ReviewChoice<OcrEvidenceRole> choice,
            }) return;
        item.Region.EvidenceRole = choice.Value;
        if (choice.Value != OcrEvidenceRole.None && !IsRelevant(item.Region)) item.Region.IsOcrEvidence = true;
        CompleteEdit();
    }

    private void SpatialRule_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_binding || sender is not ComboBox
            {
                DataContext: ReviewDetectedTextItem item,
                SelectedItem: ReviewChoice<OcrSpatialSelector> choice,
            }) return;

        item.Region.SpatialSelectorOverridden = !choice.IsAutomatic;
        item.Region.SpatialSelector = choice.Value;
        if (choice.Value is OcrSpatialSelector.SameRow or OcrSpatialSelector.NearestAnchor)
        {
            item.Region.SpatialAnchorText ??= item.AnchorOptions.FirstOrDefault();
        }
        else
        {
            item.Region.SpatialAnchorText = null;
        }
        CompleteEdit();
    }

    private void SpatialAnchor_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_binding || sender is not ComboBox
            {
                DataContext: ReviewDetectedTextItem item,
                SelectedItem: string anchor,
            }) return;
        item.Region.SpatialAnchorText = anchor;
        CompleteEdit();
    }

    private void PoolMinimum_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_binding || _annotation is null || PoolMinimumCombo.SelectedItem is not int minimum) return;
        _annotation.MinimumPoolMatches = minimum;
        CompleteEdit();
    }

    private void CompleteEdit()
    {
        if (_annotation is not null && _trial is not null)
        {
            _annotation.MinimumPoolMatches = OcrEvidenceRulePolicy.ClampMinimumPoolMatches(
                _annotation.MinimumPoolMatches,
                _evidenceUniverse);
            ApplyAutomaticRules();
        }
        IntentChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(BindCandidates, DispatcherPriority.ContextIdle);
    }

    private static bool IsRelevant(OcrTextRegion region) => region.IsOcrEvidence || region.IsVisualAnchor;
}

internal sealed class ReviewDetectedTextItem
{
    private static readonly ReviewChoice<OcrEvidenceRole>[] EvidenceChoices =
    [
        new("IGNORE", OcrEvidenceRole.None),
        new("REQUIRED", OcrEvidenceRole.Required),
        new("POOL", OcrEvidenceRole.Pool),
    ];

    public ReviewDetectedTextItem(
        OcrTextRegion region,
        IReadOnlyCollection<OcrTextRegion> candidates,
        Core.Geometry.PixelRect coarseRegion)
    {
        Region = region;
        OcrSpatialSelector inferred = OcrSpatialRuleInference.Infer(region, candidates, coarseRegion);
        SpatialOptions = CreateSpatialChoices(inferred, candidates.Any(candidate => !ReferenceEquals(candidate, region)));
        AnchorOptions = candidates
            .Where(candidate => !ReferenceEquals(candidate, region))
            .Select(candidate => candidate.Text.Trim())
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(text => text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public OcrTextRegion Region { get; }
    public string Text => Region.Text;
    public string Coordinates =>
        $"[{Region.Bounds.X},{Region.Bounds.Y},{Region.Bounds.Width},{Region.Bounds.Height}]  {Region.RecognitionConfidence:P1}";
    public bool IsOcrEvidence => Region.IsOcrEvidence;
    public bool IsVisualAnchor => Region.IsVisualAnchor;
    public bool IsFuzzy => Region.MatchMode == OcrMatchMode.FuzzyPhrase;
    public bool CanUseFuzzy => Region.IsOcrEvidence &&
        Region.Text.Count(char.IsAsciiLetterOrDigit) >= OcrPhraseMatcher.MinimumFuzzyLength;
    public IReadOnlyList<ReviewChoice<OcrEvidenceRole>> EvidenceOptions => EvidenceChoices;
    public ReviewChoice<OcrEvidenceRole> SelectedEvidence => EvidenceChoices.First(choice => choice.Value == Region.EvidenceRole);
    public IReadOnlyList<ReviewChoice<OcrSpatialSelector>> SpatialOptions { get; }
    public ReviewChoice<OcrSpatialSelector> SelectedSpatial => Region.SpatialSelectorOverridden
        ? SpatialOptions.First(choice => !choice.IsAutomatic && choice.Value == Region.SpatialSelector)
        : SpatialOptions[0];
    public IReadOnlyList<string> AnchorOptions { get; }
    public string? SelectedAnchor => Region.SpatialAnchorText;
    public Visibility AnchorVisibility => Region.SpatialSelector is OcrSpatialSelector.SameRow or OcrSpatialSelector.NearestAnchor
        ? Visibility.Visible
        : Visibility.Collapsed;

    private static ReviewChoice<OcrSpatialSelector>[] CreateSpatialChoices(
        OcrSpatialSelector inferred,
        bool hasAnchor) =>
    [
        new($"AUTO · {Label(inferred)}", inferred, true),
        new("ANY", OcrSpatialSelector.Any),
        new("LEFTMOST", OcrSpatialSelector.Leftmost),
        new("RIGHTMOST", OcrSpatialSelector.Rightmost),
        new("TOPMOST", OcrSpatialSelector.Topmost),
        new("LOWEST", OcrSpatialSelector.Bottommost),
        .. hasAnchor
            ? [new("SAME ROW", OcrSpatialSelector.SameRow), new("NEAREST", OcrSpatialSelector.NearestAnchor)]
            : Array.Empty<ReviewChoice<OcrSpatialSelector>>(),
    ];

    private static string Label(OcrSpatialSelector selector) => selector switch
    {
        OcrSpatialSelector.Bottommost => "LOWEST",
        OcrSpatialSelector.SameRow => "SAME ROW",
        OcrSpatialSelector.NearestAnchor => "NEAREST",
        _ => selector.ToString().ToUpperInvariant(),
    };
}

internal sealed record ReviewChoice<T>(string Label, T Value, bool IsAutomatic = false)
{
    public override string ToString() => Label;
}
