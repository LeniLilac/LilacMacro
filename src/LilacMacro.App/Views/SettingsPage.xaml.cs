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
    private readonly DiscordWebhookClient _discord = new();
    private readonly PrivacySettingsPanel _privacySettingsPanel;
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private MacroKeyBinding? _capturingBinding;
    private bool _updatingDisplayControls;
    private bool _updatingThemeControls;
    private bool _refreshingDiagnosticsControls;

    internal event Action<VerifiedUpdateRelease>? UpdateAvailable;

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
        _privacySettingsPanel = new PrivacySettingsPanel(ownerState);
        PrivacySettingsHost.Content = _privacySettingsPanel;
        MacroVersionText.Text = BuildVersion();
        LayoutProfileCombo.ItemsSource = new[] { "1920 x 1080 - full dock", "1366 x 768 - compact" };
        MinimizeBehaviorCombo.ItemsSource = new[] { "Keep visible", "Minimize while running", "Minimize on app start" };
        MacroLayoutProfile effectiveLayout = ownerState.LayoutProfile;
        if (MacroInstanceContext.Current.IsManagedRunner)
        {
            (int width, int height) = WindowsDesktopMetrics.PrimaryDisplaySize();
            effectiveLayout = MacroDisplayPolicy.ManagedViewportLayout(width, height);
        }
        LayoutProfileCombo.SelectedIndex = effectiveLayout == MacroLayoutProfile.Compact1366x768 ? 1 : 0;
        MinimizeBehaviorCombo.SelectedIndex = (int)MacroDisplayPolicy.EffectiveMinimizeBehavior(
            effectiveLayout,
            ownerState.MinimizeBehavior);
        CheckUpdatesOnStartupCheck.IsChecked = ownerState.CheckForUpdatesOnStartup;
        IncludePrereleaseCheck.IsChecked = ownerState.IncludePrereleaseUpdates;
        LocalInstancesPanel.Initialize(instanceManager, ownerState);
        KeyBindingsItems.ItemsSource = ownerState.KeyBindings.Items;
        PrivateServerText.Text = ownerState.PrivateServerLink;
        WebhookPassword.Password = ownerState.DiscordWebhook;
        DiscordUserIdText.Text = ownerState.DiscordUserId;
        NotifyTerminalFailureCheck.IsChecked = ownerState.NotifyOnTerminalFailure;
        NotifyRunStartCheck.IsChecked = ownerState.NotifyOnRunStart;
        NotifyRunStopCheck.IsChecked = ownerState.NotifyOnRunStop;
        NotifyTaskChangeCheck.IsChecked = ownerState.NotifyOnTaskChange;
        NotifyVictoryCheck.IsChecked = ownerState.NotifyOnVictory;
        NotifyDefeatCheck.IsChecked = ownerState.NotifyOnDefeat;
        NotifyRecoveryCheck.IsChecked = ownerState.NotifyOnRecovery;
        RefreshDiagnosticsControls();
        _initialized = true;
        RefreshDisplayControls();
        RefreshUpdateOwnership();
        _deepDebug.ArchiveSaved += DeepDebug_OnArchiveSaved;
        _ownerState.PrivacyOptionsChanged += OwnerState_OnPrivacyOptionsChanged;
        RefreshThemeControls();
    }

    private void ThemeButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        AppTheme mode = _ownerState.ThemeMode == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        ApplyAppearance(mode, _ownerState.ColorTheme);
    }

    private void ThemePaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _updatingThemeControls || ThemePaletteList.SelectedItem is not ThemeSwatchOption option) return;
        ApplyAppearance(_ownerState.ThemeMode, option.Theme);
    }

    private void ApplyAppearance(AppTheme mode, AppColorTheme colorTheme)
    {
        _ownerState.SetAppearance(mode, colorTheme);
        AppThemeManager.Apply(mode, colorTheme);
        RefreshThemeControls();
        GeneralStatusText.Text = "Appearance saved";
    }

    private void RefreshThemeControls()
    {
        _updatingThemeControls = true;
        bool isLight = _ownerState.ThemeMode == AppTheme.Light;
        ThemeButtonText.Text = isLight ? "LIGHT MODE" : "DARK MODE";
        ThemeIcon.Data = (Geometry)FindResource(isLight ? "Lucide.Sun" : "Lucide.Moon");
        IReadOnlyList<ThemeSwatchOption> swatches = AppPaletteCatalog.CreateSwatches(_ownerState.ThemeMode);
        ThemePaletteList.ItemsSource = swatches;
        ThemePaletteList.SelectedItem = swatches.First(option => option.Theme == _ownerState.ColorTheme);
        _updatingThemeControls = false;
    }

    private void GeneralTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(GeneralTabButton, GeneralPanel);
    private void RobloxTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(RobloxTabButton, RobloxPanel);
    private void DiscordTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(DiscordTabButton, DiscordPanel);
    private void KeybindsTab_OnClick(object sender, RoutedEventArgs eventArgs) => ShowPanel(KeybindsTabButton, KeybindsPanel);
    private void DiagnosticsTab_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _deepDebug.RefreshOptions();
        RefreshDiagnosticsControls();
        ShowPanel(DiagnosticsTabButton, DiagnosticsPanel);
    }

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

    internal async Task CheckAutomaticallyAsync(CancellationToken cancellationToken = default)
    {
        if (!await _ownerState.IsOnlineFeaturesDurablyEnabledAsync()
            || !_ownerState.CheckForUpdatesOnStartup
            || MacroInstanceContext.Current.IsManagedRunner) return;
        await CheckUpdatesAsync(showErrors: false, cancellationToken);
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await CheckUpdatesAsync(showErrors: true);

    private async Task CheckUpdatesAsync(
        bool showErrors,
        CancellationToken cancellationToken = default)
    {
        if (!await _updateCheckGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            if (!await _ownerState.IsOnlineFeaturesDurablyEnabledAsync())
            {
                GeneralStatusText.Text = "Online features are disabled";
                return;
            }
            CheckUpdatesButton.IsEnabled = false;
            InstallUpdateButton.Visibility = Visibility.Collapsed;
            GeneralStatusText.Text = "Checking official GitHub Releases...";
            VerifiedUpdateRelease? release = await _updates.CheckAsync(
                _ownerState.IncludePrereleaseUpdates,
                cancellationToken);
            if (release is null)
            {
                GeneralStatusText.Text = $"Version {_updates.CurrentVersion} is current";
                return;
            }
            GeneralStatusText.Text = _updates.CanInstall
                ? $"Version {release.Version} is available"
                : $"Version {release.Version} is available; install from the Program Files build";
            InstallUpdateButton.Content = $"UPDATE {release.Version}";
            InstallUpdateButton.Visibility = _updates.CanInstall ? Visibility.Visible : Visibility.Collapsed;
            if (_updates.CanInstall) UpdateAvailable?.Invoke(release);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            GeneralStatusText.Text = showErrors
                ? $"Update check failed: {exception.Message}"
                : "Automatic update check unavailable";
        }
        finally
        {
            RefreshUpdateOwnership();
            _updateCheckGate.Release();
        }
    }

    private async void InstallUpdate_OnClick(object sender, RoutedEventArgs eventArgs)
        => await InstallAvailableUpdateAsync(showConfirmation: true);

    internal async Task InstallAvailableUpdateAsync(bool showConfirmation)
    {
        VerifiedUpdateRelease? release = _updates.AvailableRelease;
        if (release is null) return;
        if (showConfirmation)
        {
            UpdateConfirmationWindow confirmation = new(release)
            {
                Owner = Window.GetWindow(this),
            };
            if (confirmation.ShowDialog() != true) return;
        }

        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        GeneralStatusText.Text = "Downloading and verifying the project-signed installer...";
        try
        {
            await _ownerState.FlushAsync();
            await _updates.LaunchAvailableUpdateAsync();
            GeneralStatusText.Text = "Update started; LilacMacro will close and reopen automatically";
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
        if (CheckUpdatesOnStartupCheck.IsChecked == true)
            _ = CheckAutomaticallyAsync();
    }

    private void DisplayOptions_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_initialized || _updatingDisplayControls) return;
        MacroLayoutProfile layout = LayoutProfileCombo.SelectedIndex == 1
            ? MacroLayoutProfile.Compact1366x768
            : MacroLayoutProfile.Full1920x1080;
        MacroMinimizeBehavior selectedMinimize =
            (MacroMinimizeBehavior)Math.Clamp(MinimizeBehaviorCombo.SelectedIndex, 0, 2);
        MacroMinimizeBehavior minimize = MacroDisplayPolicy.ConfiguredMinimizeBehaviorForSelection(
            _ownerState.LayoutProfile,
            layout,
            selectedMinimize,
            _ownerState.MinimizeBehavior);
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
            MinimizeBehaviorCombo.SelectedIndex = compact
                ? (int)MacroMinimizeBehavior.WhileRunning
                : (int)_ownerState.MinimizeBehavior;
            bool managedRunner = MacroInstanceContext.Current.IsManagedRunner;
            LayoutProfileCombo.IsEnabled = !managedRunner;
            MinimizeBehaviorCombo.IsEnabled = !managedRunner && !compact;
            CompactLayoutNote.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updatingDisplayControls = false; }
    }

    private void RefreshUpdateOwnership()
    {
        bool owner = !MacroInstanceContext.Current.IsManagedRunner;
        bool enabled = owner && _ownerState.OnlineFeaturesEnabled;
        CheckUpdatesOnStartupCheck.IsEnabled = enabled;
        IncludePrereleaseCheck.IsEnabled = enabled;
        CheckUpdatesButton.IsEnabled = enabled;
        if (!owner) GeneralStatusText.Text = "Updates are coordinated from This desktop";
    }

    private void OwnerState_OnPrivacyOptionsChanged(object? sender, EventArgs eventArgs)
    {
        RefreshUpdateOwnership();
        GeneralStatusText.Text = _ownerState.OnlineFeaturesEnabled
            ? "Privacy choices saved"
            : "Online features are disabled";
    }
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

    private static string BuildVersion() => typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

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

    private void WebhookPassword_OnPasswordChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        try
        {
            _ownerState.SetDiscordWebhook(WebhookPassword.Password);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            AppToastService.ShowError("WEBHOOK SAVE FAILED", exception.Message);
        }
    }

    private void DiscordEventOptions_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!_initialized) return;
        _ownerState.SetDiscordEventOptions(
            DiscordUserIdText.Text,
            NotifyRunStartCheck.IsChecked == true,
            NotifyRunStopCheck.IsChecked == true,
            NotifyTaskChangeCheck.IsChecked == true,
            NotifyVictoryCheck.IsChecked == true,
            NotifyDefeatCheck.IsChecked == true,
            NotifyRecoveryCheck.IsChecked == true,
            NotifyTerminalFailureCheck.IsChecked == true);
    }

    private async void TestWebhook_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        TestWebhookButton.IsEnabled = false;
        try
        {
            _ownerState.SetDiscordWebhook(WebhookPassword.Password);
            await _ownerState.FlushAsync();
            await _discord.SendTestAsync(_ownerState.DiscordWebhook, MacroInstanceContext.Current.DisplayName);
            AppToastService.ShowSuccess("WEBHOOK READY", "Test delivered.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException
            or HttpRequestException or TaskCanceledException or System.ComponentModel.Win32Exception
            or System.Security.Cryptography.CryptographicException)
        {
            string message = exception is TaskCanceledException
                ? "Webhook test timed out"
                : exception.Message;
            AppToastService.ShowError("WEBHOOK TEST FAILED", message);
        }
        finally
        {
            TestWebhookButton.IsEnabled = true;
        }
    }

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
        if (!_initialized || _refreshingDiagnosticsControls) return;
        bool enabled = DeepDebugCheck.IsChecked == true;
        MaximumArchiveStorageText.IsEnabled = enabled;
        await _deepDebug.UpdateOptionsAsync(enabled: enabled);
    }

    private void DeepDebug_OnArchiveSaved(object? sender, string path)
    {
        AppToastService.ShowSuccess("DEEP DEBUG LOG SAVED", Path.GetFileName(path));
    }

    private void OpenDiagnostics_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Directory.CreateDirectory(_deepDebug.DiagnosticsRoot);
        Process.Start(new ProcessStartInfo(_deepDebug.DiagnosticsRoot) { UseShellExecute = true });
    }

}
