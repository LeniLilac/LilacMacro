using System.Windows;
using System.Windows.Input;

namespace LilacMacro.App.Views;

public partial class LocalInstanceSetupConfirmationWindow : Window
{
    public LocalInstanceSetupConfirmationWindow() => InitializeComponent();

    private void Cancel_OnClick(object sender, RoutedEventArgs eventArgs) => DialogResult = false;

    private void Continue_OnClick(object sender, RoutedEventArgs eventArgs) => DialogResult = true;

    private void Dialog_OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape) DialogResult = false;
    }
}
