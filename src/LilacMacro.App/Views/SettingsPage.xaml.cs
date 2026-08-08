using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LilacMacro.Core.Automation;
using LilacMacro.App.Theming;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Runtime;
using LilacMacro.App.Notifications;

namespace LilacMacro.App.Views;

public partial class SettingsPage : UserControl
{
    private bool _initialized;
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly Action<bool> _keyCaptureStateChanged;
    private MacroKeyBinding? _capturingBinding;

    internal SettingsPage(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState,
        Action<bool> keyCaptureStateChanged)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _keyCaptureStateChanged = keyCaptureStateChanged;
        InitializeComponent();
        MinimizeBehaviorCombo.ItemsSource = new[] { "Keep visible", "Minimize while running", "Minimize on start" };
        RecoveryAttemptsCombo.ItemsSource = new[] { "0", "1", "2", "3" };
        UiScaleCombo.ItemsSource = new[] { "90%", "100%", "110%" };
        GameProfileCombo.ItemsSource = new[] { "Required defaults", "Custom", "Do not prepare" };
        UpdateChannelCombo.ItemsSource = new[] { "Stable", "Prerelease" };
        CaptureIntervalCombo.ItemsSource = new[] { "0.5 sec", "1.0 sec", "2.0 sec" };
        MinimizeBehaviorCombo.SelectedIndex = 1;
        RecoveryAttemptsCombo.SelectedIndex = 2;
        UiScaleCombo.SelectedIndex = 1;
        GameProfileCombo.SelectedIndex = 0;
        UpdateChannelCombo.SelectedIndex = 0;
        CaptureIntervalCombo.SelectedIndex = 1;
        KeyBindingsItems.ItemsSource = ownerState.KeyBindings.Items;
        _initialized = true;
        PrivateServerText.Text = ownerState.PrivateServerLink;
        DeepDebugCheck.IsChecked = _deepDebug.Options.Enabled;
        FrameHistoryText.Text = _deepDebug.Options.FrameRetentionMinutes.ToString();
        FrameHistoryText.IsEnabled = _deepDebug.Options.Enabled;
        _deepDebug.ArchiveSaved += DeepDebug_OnArchiveSaved;
        RefreshThemeButton();
    }

    private void ThemeButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        AppThemeManager.Toggle();
        RefreshThemeButton();
    }

    private void RefreshThemeButton()
    {
        bool isLight = AppThemeManager.Current == AppTheme.Light;
        ThemeButtonText.Text = isLight ? "DARK MODE" : "LIGHT MODE";
        ThemeIcon.Data = (Geometry)FindResource(isLight ? "Lucide.Moon" : "Lucide.Sun");
    }

    private void GeneralTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(GeneralTabButton, GeneralPanel);
    private void RobloxTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(RobloxTabButton, RobloxPanel);
    private void DiscordTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(DiscordTabButton, DiscordPanel);
    private void KeybindsTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(KeybindsTabButton, KeybindsPanel);
    private void DiagnosticsTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(DiagnosticsTabButton, DiagnosticsPanel);

    private void ShowPanel(Button selectedButton, UIElement selectedPanel)
    {
        FinishKeyCapture();
        Button[] buttons = [GeneralTabButton, RobloxTabButton, DiscordTabButton, KeybindsTabButton, DiagnosticsTabButton];
        UIElement[] panels = [GeneralPanel, RobloxPanel, DiscordPanel, KeybindsPanel, DiagnosticsPanel];
        foreach (Button button in buttons) button.Tag = string.Empty;
        foreach (UIElement panel in panels) panel.Visibility = Visibility.Collapsed;
        selectedButton.Tag = "Active";
        selectedPanel.Visibility = Visibility.Visible;
    }

    private void SettingChanged_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_initialized) GeneralStatusText.Text = "Changed in this session";
    }

    private void SettingChanged_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_initialized) GeneralStatusText.Text = "Profile changed in this session";
    }

    private void CheckUpdates_OnClick(object sender, RoutedEventArgs eventArgs) => GeneralStatusText.Text = "Update check is not connected";
    private void LocalPath_OnClick(object sender, RoutedEventArgs eventArgs) => GeneralStatusText.Text = "Folder opening is not connected";
    private void TestPrivateServer_OnClick(object sender, RoutedEventArgs eventArgs) => PrivateServerStatusText.Text = "Private-server test is not connected";

    private void PrivateServerText_OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        _ownerState.PrivateServerLink = PrivateServerText.Text.Trim();
        PrivateServerStatusText.Text = string.IsNullOrWhiteSpace(_ownerState.PrivateServerLink) ? "Not stored" : "Ready this session";
    }
    private void TestWebhook_OnClick(object sender, RoutedEventArgs eventArgs) => WebhookStatusText.Text = "Webhook test is not connected";

    private void KeyBindingButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: MacroKeyBinding binding } button) return;
        FinishKeyCapture();
        _capturingBinding = binding;
        binding.SetCapturing(true);
        _keyCaptureStateChanged(true);
        Keyboard.Focus(button);
    }

    private void SettingsPage_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (_capturingBinding is null) return;
        eventArgs.Handled = true;
        Key key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;
        if (key == Key.Escape)
        {
            FinishKeyCapture();
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!KeyboardKey.IsSupportedAutomationKey(virtualKey))
        {
            AppToastService.ShowError("KEY NOT SUPPORTED", "Choose a keyboard key supported by Windows input.");
            return;
        }
        MacroKeyBinding macroToggle = _ownerState.KeyBindings[MacroKeyBindingId.MacroToggle];
        bool conflictsWithGlobal = _capturingBinding.Id == MacroKeyBindingId.MacroToggle
            ? _ownerState.KeyBindings.Items.Any(binding =>
                binding.Id != MacroKeyBindingId.MacroToggle && binding.VirtualKey == virtualKey)
            : macroToggle.VirtualKey == virtualKey;
        if (conflictsWithGlobal)
        {
            AppToastService.ShowError("KEY ALREADY USED", "Macro start / stop must use a different key from Roblox actions.");
            return;
        }
        _capturingBinding.SetVirtualKey(virtualKey);
        _capturingBinding = null;
        _keyCaptureStateChanged(false);
    }

    private void SettingsPage_OnUnloaded(object sender, RoutedEventArgs eventArgs) => FinishKeyCapture();

    private void UnsetBinding_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: MacroKeyBinding binding } || !binding.CanUnset) return;
        FinishKeyCapture();
        binding.Unset();
    }

    private void ResetBindings_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        FinishKeyCapture();
        _ownerState.KeyBindings.Reset();
    }

    private void FinishKeyCapture()
    {
        if (_capturingBinding is null) return;
        _capturingBinding.SetCapturing(false);
        _capturingBinding = null;
        _keyCaptureStateChanged(false);
    }

    private async void DeepDebug_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        bool enabled = DeepDebugCheck.IsChecked == true;
        FrameHistoryText.IsEnabled = enabled;
        int retention = int.TryParse(FrameHistoryText.Text, out int parsed)
            ? parsed
            : _deepDebug.Options.FrameRetentionMinutes;
        await _deepDebug.UpdateOptionsAsync(enabled, retention);
        DiagnosticsStatusText.Text = enabled
            ? $"Deep debug enabled · {_deepDebug.Options.FrameRetentionMinutes} minute frame history"
            : "Deep debug disabled";
    }

    private void DeepDebug_OnArchiveSaved(object? sender, string path)
    {
        _ = Dispatcher.BeginInvoke(() =>
            DiagnosticsStatusText.Text = $"Saved {Path.GetFileName(path)}");
    }

    private async void FrameHistory_OnLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        int retention = int.TryParse(FrameHistoryText.Text, out int parsed)
            ? parsed
            : _deepDebug.Options.FrameRetentionMinutes;
        await _deepDebug.UpdateOptionsAsync(DeepDebugCheck.IsChecked == true, retention);
        FrameHistoryText.Text = _deepDebug.Options.FrameRetentionMinutes.ToString();
    }

    private void OpenDiagnostics_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Directory.CreateDirectory(_deepDebug.DiagnosticsRoot);
        Process.Start(new ProcessStartInfo(_deepDebug.DiagnosticsRoot) { UseShellExecute = true });
    }

    private void ManualRecording_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        bool enabled = ManualRecordingCheck.IsChecked == true;
        RecordingNameText.IsEnabled = enabled;
        DiagnosticsStatusText.Text = enabled ? "Recording controls enabled" : "No recording armed";
    }

    private void ArmRecording_OnClick(object sender, RoutedEventArgs eventArgs) =>
        DiagnosticsStatusText.Text = ManualRecordingCheck.IsChecked == true ? $"Armed: {RecordingNameText.Text}" : "Enable recording controls first";
}
