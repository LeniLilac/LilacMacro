using System.Diagnostics;
using System.Windows;
using LilacMacro.App.Runtime;

namespace LilacMacro.App.Views;

public partial class PrivacySettingsPanel
{
    private readonly MacroOwnerState _ownerState;
    private bool _initialized;

    internal PrivacySettingsPanel(MacroOwnerState ownerState)
    {
        _ownerState = ownerState;
        InitializeComponent();
        OnlineFeaturesCheck.IsChecked = ownerState.OnlineFeaturesEnabled;
        TelemetryCheck.IsChecked = ownerState.TelemetryEnabled;
        AutomaticReportsCheck.IsChecked = ownerState.AutomaticErrorReportsEnabled;
        _initialized = true;
    }

    private async void Choice_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        IsEnabled = false;
        SavedText.Text = "Saving choices...";
        try
        {
            PrivacyChoiceKind kind = ReferenceEquals(sender, OnlineFeaturesCheck)
                ? PrivacyChoiceKind.OnlineFeatures
                : ReferenceEquals(sender, TelemetryCheck)
                    ? PrivacyChoiceKind.Telemetry
                    : PrivacyChoiceKind.AutomaticErrorReports;
            bool enabled = sender is System.Windows.Controls.CheckBox checkBox
                && checkBox.IsChecked == true;
            await _ownerState.SavePrivacyChoiceAsync(kind, enabled);
            _initialized = false;
            OnlineFeaturesCheck.IsChecked = _ownerState.OnlineFeaturesEnabled;
            TelemetryCheck.IsChecked = _ownerState.TelemetryEnabled;
            AutomaticReportsCheck.IsChecked = _ownerState.AutomaticErrorReportsEnabled;
            _initialized = true;
            SavedText.Text = "Choices saved locally";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _initialized = false;
            OnlineFeaturesCheck.IsChecked = _ownerState.OnlineFeaturesEnabled;
            TelemetryCheck.IsChecked = _ownerState.TelemetryEnabled;
            AutomaticReportsCheck.IsChecked = _ownerState.AutomaticErrorReportsEnabled;
            SavedText.Text = "Not saved. Network choices remain off where required; try again.";
        }
        finally
        {
            _initialized = true;
            IsEnabled = true;
        }
    }

    private void Privacy_OnClick(object sender, RoutedEventArgs eventArgs) => Open(PrivacyChoicesPolicy.PrivacyUri);

    private void Terms_OnClick(object sender, RoutedEventArgs eventArgs) => Open(PrivacyChoicesPolicy.TermsUri);

    private static void Open(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
    {
        UseShellExecute = true,
    });
}
