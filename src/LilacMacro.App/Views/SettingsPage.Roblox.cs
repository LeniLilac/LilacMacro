using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Notifications;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;
using LilacMacro.Windows;

namespace LilacMacro.App.Views;

public partial class SettingsPage
{
    private void LocalPath_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string folderKind }) return;
        string path = folderKind switch
        {
            "data" => MacroInstanceContext.Current.ConfigurationRoot,
            "logs" => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LilacMacro",
                "logs"),
            _ => throw new InvalidOperationException("The requested LilacMacro folder is unknown."),
        };

        try
        {
            Directory.CreateDirectory(path);
            _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })
                ?? throw new InvalidOperationException("Windows did not open the requested folder.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            AppToastService.ShowError("FOLDER OPEN FAILED", exception.Message);
        }
    }

    private async void TestPrivateServer_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        TestPrivateServerButton.IsEnabled = false;
        try
        {
            RobloxPrivateServerLaunchTarget target = RobloxPrivateServerLaunchTarget.Parse(PrivateServerText.Text);
            await new RobloxProtocolLauncher().LaunchAsync(target.LaunchUri, CancellationToken.None);
            AppToastService.ShowSuccess("PRIVATE SERVER READY", "Roblox launch requested.");
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            AppToastService.ShowError("PRIVATE SERVER TEST FAILED", exception.Message);
        }
        finally
        {
            TestPrivateServerButton.IsEnabled = true;
        }
    }

    private void PrivateServerText_OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        try
        {
            _ownerState.SetPrivateServerLink(PrivateServerText.Text);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            AppToastService.ShowError("PRIVATE SERVER SAVE FAILED", exception.Message);
        }
    }
}
