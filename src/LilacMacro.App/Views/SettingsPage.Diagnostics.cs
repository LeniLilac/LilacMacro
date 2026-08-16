using System.Windows;

namespace LilacMacro.App.Views;

public partial class SettingsPage
{
    private async void AutomaticCleanup_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        await _deepDebug.UpdateOptionsAsync(
            DeepDebugCheck.IsChecked == true,
            _deepDebug.Options.FrameRetentionMinutes,
            AutomaticCleanupCheck.IsChecked == true);
        DiagnosticsStatusText.Text = AutomaticCleanupCheck.IsChecked == true
            ? "Local archive cleanup enabled"
            : "Local archives kept until you delete them";
    }
}
