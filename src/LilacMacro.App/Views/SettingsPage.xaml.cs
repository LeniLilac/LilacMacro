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
using LilacMacro.Windows;

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
        LocalInstanceManagerController instanceManager,
        Action<bool> keyCaptureStateChanged)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _keyCaptureStateChanged = keyCaptureStateChanged;
        InitializeComponent();
        MacroVersionText.Text = BuildVersion();
        MinimizeBehaviorCombo.ItemsSource = new[] { "Keep visible", "Minimize while running", "Minimize on start" };
        UpdateChannelCombo.ItemsSource = new[] { "Stable", "Prerelease" };
        CaptureIntervalCombo.ItemsSource = new[] { "0.5 sec", "1.0 sec", "2.0 sec" };
        MinimizeBehaviorCombo.SelectedIndex = 1;
        UpdateChannelCombo.SelectedIndex = 0;
        CaptureIntervalCombo.SelectedIndex = 1;
        LocalInstancesPanel.Initialize(instanceManager, ownerState);
        KeyBindingsItems.ItemsSource = ownerState.KeyBindings.Items;
        PrivateServerPassword.Password = ownerState.PrivateServerLink;
        WebhookPassword.Password = ownerState.DiscordWebhook;
        DiscordUserIdText.Text = ownerState.DiscordUserId;
        NotifyTerminalFailureCheck.IsChecked = ownerState.NotifyOnTerminalFailure;
        IncludeFailureDetailsCheck.IsChecked = ownerState.IncludeFailureDetails;
        DeepDebugCheck.IsChecked = _deepDebug.Options.Enabled;
        FrameHistoryText.Text = _deepDebug.Options.FrameRetentionMinutes.ToString();
        FrameHistoryText.IsEnabled = _deepDebug.Options.Enabled;
        PrivateServerStatusText.Text = ownerState.PrivateServerLink.Length == 0 ? "Not configured" : "Stored securely";
        WebhookStatusText.Text = ownerState.DiscordWebhook.Length == 0 ? "Not configured" : "Stored securely";
        _initialized = true;
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
    private async void TestPrivateServer_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            RobloxPrivateServerLaunchTarget target = RobloxPrivateServerLaunchTarget.Parse(PrivateServerPassword.Password);
            await new RobloxProtocolLauncher().LaunchAsync(target.LaunchUri, CancellationToken.None);
            PrivateServerStatusText.Text = "Roblox launch requested";
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            PrivateServerStatusText.Text = exception.Message;
        }
    }

    private static string BuildVersion() => typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private void PrivateServerPassword_OnPasswordChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        try
        {
            _ownerState.SetPrivateServerLink(PrivateServerPassword.Password);
            PrivateServerStatusText.Text = _ownerState.PrivateServerLink.Length == 0 ? "Not configured" : "Stored securely";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            AppToastService.ShowError("PRIVATE SERVER SAVE FAILED", exception.Message);
        }
    }

    private void WebhookPassword_OnPasswordChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        try
        {
            _ownerState.SetDiscordWebhook(WebhookPassword.Password);
            WebhookStatusText.Text = _ownerState.DiscordWebhook.Length == 0 ? "Not configured" : "Stored securely";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            AppToastService.ShowError("WEBHOOK SAVE FAILED", exception.Message);
        }
    }

    private void DiscordFailureOptions_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        _ownerState.SetDiscordFailureOptions(
            DiscordUserIdText.Text,
            NotifyTerminalFailureCheck.IsChecked == true,
            IncludeFailureDetailsCheck.IsChecked == true);
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

    private async void SettingsPage_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
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
        await PersistKeyBindingsAsync();
    }

    private void SettingsPage_OnUnloaded(object sender, RoutedEventArgs eventArgs) => FinishKeyCapture();

    private async void UnsetBinding_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: MacroKeyBinding binding } || !binding.CanUnset) return;
        FinishKeyCapture();
        binding.Unset();
        await PersistKeyBindingsAsync();
    }

    private async void ResetBindings_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        FinishKeyCapture();
        _ownerState.KeyBindings.Reset();
        await PersistKeyBindingsAsync();
    }

    private async Task PersistKeyBindingsAsync()
    {
        try
        {
            await _ownerState.FlushAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppToastService.ShowError("KEYBIND SAVE FAILED", exception.Message);
        }
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
