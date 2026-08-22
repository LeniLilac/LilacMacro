using System.Windows;
using System.Windows.Input;
using LilacMacro.App.Notifications;

namespace LilacMacro.App.Views;

public partial class SettingsPage
{
    private Guid? _installationId;

    private async Task LoadInstallationIdentityAsync()
    {
        try
        {
            _installationId = await _installation.GetOrCreateAsync();
            InstallationIdText.Text = _installationId.Value.ToString("D");
            CopyInstallationIdButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            InstallationIdText.Text = "Unavailable";
            CopyInstallationIdButton.IsEnabled = false;
            AppToastService.ShowError("INSTALLATION ID UNAVAILABLE", exception.Message);
        }
    }

    private void CopyInstallationId_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_installationId is not Guid installationId) return;
        try
        {
            Clipboard.SetText(installationId.ToString("D"));
            AppToastService.ShowSuccess("INSTALLATION ID COPIED", "Ready to paste.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Keyboard.Focus(InstallationIdText);
            InstallationIdText.SelectAll();
            AppToastService.ShowError(
                "CLIPBOARD BUSY",
                "The installation ID is selected for manual copying.");
        }
    }
}
