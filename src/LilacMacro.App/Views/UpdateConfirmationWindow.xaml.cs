using System.Windows;
using System.Windows.Input;
using LilacMacro.Core.Updates;

namespace LilacMacro.App.Views;

public partial class UpdateConfirmationWindow : Window
{
    public UpdateConfirmationWindow(VerifiedUpdateRelease release)
    {
        InitializeComponent();
        UpdateDescription.Text = $"LilacMacro will close every open Macro window on this machine, install version {release.Version}, then reopen the active desktops.";
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs eventArgs) => DialogResult = false;

    private void Update_OnClick(object sender, RoutedEventArgs eventArgs) => DialogResult = true;

    private void Dialog_OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape) DialogResult = false;
    }
}
