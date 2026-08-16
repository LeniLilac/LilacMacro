using System.Windows;

namespace LilacMacro.App.Views;

public partial class PlanPage
{
    private void SharePlan_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        PlanSharePanel panel = new(_ownerState, _selectedPlan);
        panel.CloseRequested += SharePanel_OnCloseRequested;
        ShareEditorHost.Content = panel;
        ShareEditorOverlay.Visibility = Visibility.Visible;
    }

    private void SharePanel_OnCloseRequested(object? sender, EventArgs eventArgs) => CloseShareEditor();

    private void CloseShareEditor()
    {
        if (ShareEditorHost.Content is PlanSharePanel panel)
            panel.CloseRequested -= SharePanel_OnCloseRequested;
        ShareEditorHost.Content = null;
        ShareEditorOverlay.Visibility = Visibility.Collapsed;
    }
}
