using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LilacMacro.Core.Automation;
using LilacMacro.App.Theming;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Runtime;
using LilacMacro.App.Notifications;
using LilacMacro.App.Updates;
using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Updates;
using LilacMacro.Windows;

namespace LilacMacro.App.Views;

public partial class SettingsPage : UserControl
{
    private bool _initialized;
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly Action<bool> _keyCaptureStateChanged;
    private readonly ApplicationUpdateService _updates;
    private MacroKeyBinding? _capturingBinding;
    private bool _updatingDisplayControls;

    internal SettingsPage(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState,
        LocalInstanceManagerController instanceManager,
        ApplicationUpdateService updates,
        Action<bool> keyCaptureStateChanged)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _updates = updates;
        _keyCaptureStateChanged = keyCaptureStateChanged;
        InitializeComponent();
        MacroVersionText.Text = BuildVersion();
        LayoutProfileCombo.ItemsSource = new[] { "1920 x 1080 - full dock", "1366 x 768 - compact" };
        MinimizeBehaviorCombo.ItemsSource = new[] { "Keep visible", "Minimize while running", "Minimize on app start" };
        CaptureIntervalCombo.ItemsSource = new[] { "0.5 sec", "1.0 sec", "2.0 sec" };
        LayoutProfileCombo.SelectedIndex = ownerState.LayoutProfile == MacroLayoutProfile.Compact1366x768 ? 1 : 0;
        MinimizeBehaviorCombo.SelectedIndex = (int)ownerState.EffectiveMinimizeBehavior;
        CheckUpdatesOnStartupCheck.IsChecked = ownerState.CheckForUpdatesOnStartup;
        IncludePrereleaseCheck.IsChecked = ownerState.IncludePrereleaseUpdates;
        CaptureIntervalCombo.SelectedIndex = 1;
        LocalInstancesPanel.Initialize(instanceManager, ownerState);
        KeyBindingsItems.ItemsSource = ownerState.KeyBindings.Items;
        PrivateServerText.Text = ownerState.PrivateServerLink;
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
        RefreshDisplayControls();
        RefreshUpdateOwnership();
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

    internal async Task CheckOnStartupAsync()
    {
        if (!_ownerState.CheckForUpdatesOnStartup || MacroInstanceContext.Current.IsManagedRunner) return;
        await CheckUpdatesAsync(showErrors: false);
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await CheckUpdatesAsync(showErrors: true);

    private async Task CheckUpdatesAsync(bool showErrors)
    {
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        GeneralStatusText.Text = "Checking official GitHub Releases...";
        try
        {
            VerifiedUpdateRelease? release = await _updates.CheckAsync(_ownerState.IncludePrereleaseUpdates);
            if (release is null)
            {
                GeneralStatusText.Text = $"Version {_updates.CurrentVersion} is current";
                return;
            }
            GeneralStatusText.Text = _updates.CanInstall
                ? $"Version {release.Version} is available"
                : $"Version {release.Version} is available; install from the Program Files build";
            InstallUpdateButton.Content = $"INSTALL {release.Version}";
            InstallUpdateButton.Visibility = _updates.CanInstall ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            GeneralStatusText.Text = showErrors
                ? $"Update check failed: {exception.Message}"
                : "Automatic update check unavailable";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = !MacroInstanceContext.Current.IsManagedRunner;
        }
    }

    private async void InstallUpdate_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        GeneralStatusText.Text = "Downloading and verifying the signed installer...";
        try
        {
            await _ownerState.FlushAsync();
            await _updates.LaunchAvailableUpdateAsync();
            GeneralStatusText.Text = "Installer started; LilacMacro will close when installation begins";
        }
        catch (OperationCanceledException exception)
        {
            GeneralStatusText.Text = exception.Message;
            CheckUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            GeneralStatusText.Text = $"Update failed: {exception.Message}";
            CheckUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void UpdateOptions_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        _ownerState.SetUpdateOptions(
            CheckUpdatesOnStartupCheck.IsChecked == true,
            IncludePrereleaseCheck.IsChecked == true);
        GeneralStatusText.Text = "Update options saved";
    }

    private void DisplayOptions_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _updatingDisplayControls) return;
        MacroLayoutProfile layout = LayoutProfileCombo.SelectedIndex == 1
            ? MacroLayoutProfile.Compact1366x768
            : MacroLayoutProfile.Full1920x1080;
        MacroMinimizeBehavior minimize = (MacroMinimizeBehavior)Math.Clamp(MinimizeBehaviorCombo.SelectedIndex, 0, 2);
        if (layout == MacroLayoutProfile.Compact1366x768) minimize = MacroMinimizeBehavior.WhileRunning;
        _ownerState.SetDisplayOptions(layout, minimize);
        RefreshDisplayControls();
        GeneralStatusText.Text = "Display options saved";
    }

    private void RefreshDisplayControls()
    {
        _updatingDisplayControls = true;
        try
        {
            bool compact = LayoutProfileCombo.SelectedIndex == 1;
            if (compact) MinimizeBehaviorCombo.SelectedIndex = (int)MacroMinimizeBehavior.WhileRunning;
            MinimizeBehaviorCombo.IsEnabled = !compact;
            CompactLayoutNote.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updatingDisplayControls = false; }
    }

    private void RefreshUpdateOwnership()
    {
        bool owner = !MacroInstanceContext.Current.IsManagedRunner;
        CheckUpdatesOnStartupCheck.IsEnabled = owner;
        IncludePrereleaseCheck.IsEnabled = owner;
        CheckUpdatesButton.IsEnabled = owner;
        if (!owner) GeneralStatusText.Text = "Updates are coordinated from This desktop";
    }
    private void LocalPath_OnClick(object sender, RoutedEventArgs eventArgs) => GeneralStatusText.Text = "Folder opening is not connected";
    private async void TestPrivateServer_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            RobloxPrivateServerLaunchTarget target = RobloxPrivateServerLaunchTarget.Parse(PrivateServerText.Text);
            await new RobloxProtocolLauncher().LaunchAsync(target.LaunchUri, CancellationToken.None);
            PrivateServerStatusText.Text = "Roblox launch requested";
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            PrivateServerStatusText.Text = exception.Message;
        }
    }

    private static string BuildVersion() => typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private void PrivateServerText_OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (!_initialized) return;
        try
        {
            _ownerState.SetPrivateServerLink(PrivateServerText.Text);
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
