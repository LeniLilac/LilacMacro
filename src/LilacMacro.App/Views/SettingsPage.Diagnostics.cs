namespace LilacMacro.App.Views;

public partial class SettingsPage
{
    private async void RetainedArchiveCount_OnLostKeyboardFocus(
        object sender,
        System.Windows.Input.KeyboardFocusChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        int retainedArchives = int.TryParse(RetainedArchiveCountText.Text, out int parsed)
            ? parsed
            : _deepDebug.Options.RetainedArchiveCount;
        await _deepDebug.UpdateOptionsAsync(
            DeepDebugCheck.IsChecked == true,
            _deepDebug.Options.FrameRetentionMinutes,
            retainedArchiveCount: retainedArchives);
        RetainedArchiveCountText.Text = _deepDebug.Options.RetainedArchiveCount.ToString();
    }
}
