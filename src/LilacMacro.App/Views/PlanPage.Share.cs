using System.Windows;

namespace LilacMacro.App.Views;

public partial class PlanPage
{
    private void SharePlan_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        PlanShareWindow window = new(_ownerState, _selectedPlan)
        {
            Owner = Window.GetWindow(this),
        };
        _ = window.ShowDialog();
    }
}
