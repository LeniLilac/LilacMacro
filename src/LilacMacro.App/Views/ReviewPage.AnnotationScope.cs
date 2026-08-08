using System.Windows;
using LilacMacro.Core.Datasets;

namespace LilacMacro.App.Views;

public partial class ReviewPage
{
    private void GlobalRegion_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_binding || CurrentAnnotation is not { } annotation || _workspace.ActiveDataset is not { } dataset) return;
        if (GlobalRegionToggle.IsChecked == true)
        {
            AnnotationScopePolicy.Promote(dataset.Manifest, _activeFrame!, annotation);
        }
        else if (annotation.GlobalGroupId is not null)
        {
            AnnotationScopePolicy.Demote(dataset.Manifest, annotation);
        }

        FrameList.Items.Refresh();
        MarkDirty();
        RenderSurfaces();
        BindInspector();
    }

    private void SynchronizeGlobalAnnotation(BoxAnnotation? annotation)
    {
        if (annotation?.GlobalGroupId is null || _workspace.ActiveDataset is not { } dataset) return;
        AnnotationScopePolicy.Synchronize(dataset.Manifest, annotation);
    }

    private IReadOnlyCollection<OcrTextRegion> EvidenceUniverse(BoxAnnotation? annotation)
    {
        if (annotation is null) return [];
        if (annotation.GlobalGroupId is not { } groupId || _workspace.ActiveDataset is not { } dataset)
        {
            return ReviewOcrSupport.Latest(annotation, SelectedOcrModel, SelectedOcrDevice)?.Regions ?? [];
        }

        return AnnotationScopePolicy.Members(dataset.Manifest, groupId)
            .Select(member => ReviewOcrSupport.Latest(member, SelectedOcrModel, SelectedOcrDevice))
            .Where(trial => trial is not null)
            .SelectMany(trial => trial!.Regions)
            .ToArray();
    }
}
