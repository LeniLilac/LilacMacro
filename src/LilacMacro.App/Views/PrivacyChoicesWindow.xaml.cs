using System.Diagnostics;
using System.Windows;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;

namespace LilacMacro.App.Views;

public partial class PrivacyChoicesWindow : Window
{
    private readonly MacroOwnerState _ownerState;

    internal PrivacyChoicesWindow(MacroOwnerState ownerState)
    {
        _ownerState = ownerState;
        InitializeComponent();
        OnlineFeaturesCheck.IsChecked = ownerState.OnlineFeaturesEnabled;
        TelemetryCheck.IsChecked = ownerState.TelemetryEnabled;
        AutomaticReportsCheck.IsChecked = ownerState.AutomaticErrorReportsEnabled;
    }

    private async void Continue_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        IsEnabled = false;
        try
        {
            await _ownerState.SavePrivacyChoicesAsync(
                OnlineFeaturesCheck.IsChecked == true,
                TelemetryCheck.IsChecked == true,
                AutomaticReportsCheck.IsChecked == true);
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            IsEnabled = true;
            AppToastService.ShowError("PRIVACY CHOICES NOT SAVED", exception.Message);
        }
    }

    private void Privacy_OnClick(object sender, RoutedEventArgs eventArgs) => Open(PrivacyChoicesPolicy.PrivacyUri);

    private void Terms_OnClick(object sender, RoutedEventArgs eventArgs) => Open(PrivacyChoicesPolicy.TermsUri);

    private static void Open(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
    {
        UseShellExecute = true,
    });
}
