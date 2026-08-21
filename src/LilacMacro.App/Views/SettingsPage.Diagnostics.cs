using LilacMacro.App.Diagnostics;

namespace LilacMacro.App.Views;

public partial class SettingsPage
{
    private void RefreshDiagnosticsControls()
    {
        _refreshingDiagnosticsControls = true;
        try
        {
            DeepDebugCheck.IsChecked = _deepDebug.Options.Enabled;
            MaximumArchiveStorageText.Text =
                _deepDebug.Options.MaximumArchiveStorageGiB.ToString();
            MaximumArchiveStorageText.IsEnabled = _deepDebug.Options.Enabled;
        }
        finally
        {
            _refreshingDiagnosticsControls = false;
        }
    }

    private async void MaximumArchiveStorage_OnLostKeyboardFocus(
        object sender,
        System.Windows.Input.KeyboardFocusChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        int maximumStorage = int.TryParse(
            MaximumArchiveStorageText.Text,
            out int parsed)
            ? parsed
            : _deepDebug.Options.MaximumArchiveStorageGiB;
        await _deepDebug.UpdateOptionsAsync(maximumArchiveStorageGiB: maximumStorage);
        MaximumArchiveStorageText.Text =
            _deepDebug.Options.MaximumArchiveStorageGiB.ToString();
    }
}
