using System.Collections.ObjectModel;
using System.ComponentModel;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Theming;
using LilacMacro.App.Views;
using LilacMacro.Core.LocalSession;
using LilacMacro.Core.Security;
using LilacMacro.Windows;

namespace LilacMacro.App.Runtime;

internal sealed class MacroOwnerState
{
    private readonly MacroSettingsStore _settingsStore;
    private readonly ISecretProtector _secretProtector;
    private readonly object _saveSync = new();
    private readonly SemaphoreSlim _privacyCommitGate = new(1, 1);
    private Task _pendingSave = Task.CompletedTask;
    private bool _privacyCommitInProgress;
    private bool _saveRequestedDuringPrivacyCommit;
    private string _encryptedPrivateServerLink;
    private string _encryptedDiscordWebhook;
    private long _privacyGeneration;

    private MacroOwnerState(
        MacroSettingsStore settingsStore,
        MacroKeyBindings keyBindings,
        ObservableCollection<PlanPrototype> plans,
        int selectedPlanIndex,
        ISecretProtector secretProtector,
        MacroSettings settings,
        long privacyGeneration)
    {
        _settingsStore = settingsStore;
        _secretProtector = secretProtector;
        _privacyGeneration = privacyGeneration;
        KeyBindings = keyBindings;
        Plans = plans;
        SelectedPlanIndex = Math.Clamp(selectedPlanIndex, 0, Plans.Count - 1);
        string encryptedPrivateServerLink = settings.EncryptedPrivateServerLink ?? string.Empty;
        string encryptedDiscordWebhook = settings.EncryptedDiscordWebhook ?? string.Empty;
        PrivateServerLink = UnprotectOrEmpty(encryptedPrivateServerLink, out bool privateServerValid);
        DiscordWebhook = UnprotectOrEmpty(encryptedDiscordWebhook, out bool webhookValid);
        _encryptedPrivateServerLink = privateServerValid ? encryptedPrivateServerLink : string.Empty;
        _encryptedDiscordWebhook = webhookValid ? encryptedDiscordWebhook : string.Empty;
        DiscordUserId = settings.DiscordUserId?.Trim() ?? string.Empty;
        NotifyOnTerminalFailure = settings.NotifyOnTerminalFailure;
        NotifyOnRunStart = settings.NotifyOnRunStart;
        NotifyOnRunStop = settings.NotifyOnRunStop;
        NotifyOnTaskChange = settings.NotifyOnTaskChange;
        NotifyOnVictory = settings.NotifyOnVictory;
        NotifyOnDefeat = settings.NotifyOnDefeat;
        NotifyOnRecovery = settings.NotifyOnRecovery;
        EnableDiagnosticUploads = settings.EnableDiagnosticUploads;
        PrivacyChoicesVersion = settings.PrivacyChoicesVersion;
        OnlineFeaturesEnabled = settings.OnlineFeaturesEnabled;
        TelemetryEnabled = settings.TelemetryEnabled;
        AutomaticErrorReportsEnabled = settings.AutomaticErrorReportsEnabled;
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        IncludePrereleaseUpdates = settings.IncludePrereleaseUpdates;
        LayoutProfile = Enum.IsDefined(settings.LayoutProfile)
            ? settings.LayoutProfile
            : MacroLayoutProfile.Full1920x1080;
        MinimizeBehavior = Enum.IsDefined(settings.MinimizeBehavior)
            ? settings.MinimizeBehavior
            : MacroMinimizeBehavior.WhileRunning;
        ThemeMode = Enum.IsDefined(settings.ThemeMode) ? settings.ThemeMode : AppTheme.Light;
        ColorTheme = Enum.IsDefined(settings.ColorTheme) ? settings.ColorTheme : AppColorTheme.Lilac;
        RunnerLayoutProfiles = (settings.RunnerLayoutProfiles ?? [])
            .Where(item => IsRunnerProfileId(item.Key) && Enum.IsDefined(item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        KeyBindings.Changed += KeyBindings_OnChanged;
    }

    public event EventHandler? SelectedPlanChanged;

    public event EventHandler? DisplayOptionsChanged;

    public event EventHandler? AppearanceChanged;

    public event EventHandler? PrivacyOptionsChanged;

    public ObservableCollection<PlanPrototype> Plans { get; }

    public int SelectedPlanIndex { get; private set; }

    public PlanPrototype SelectedPlan => Plans[SelectedPlanIndex];

    public MacroKeyBindings KeyBindings { get; }

    public ExecutionTarget ExecutionTarget => ExecutionTarget.LocalDesktop;

    public string PrivateServerLink { get; private set; }

    public string DiscordWebhook { get; private set; }

    public string DiscordUserId { get; private set; }

    public bool NotifyOnTerminalFailure { get; private set; }

    public bool NotifyOnRunStart { get; private set; }

    public bool NotifyOnRunStop { get; private set; }

    public bool NotifyOnTaskChange { get; private set; }

    public bool NotifyOnVictory { get; private set; }

    public bool NotifyOnDefeat { get; private set; }

    public bool NotifyOnRecovery { get; private set; }

    public bool EnableDiagnosticUploads { get; private set; }

    public int PrivacyChoicesVersion { get; private set; }

    public bool OnlineFeaturesEnabled { get; private set; }

    public bool TelemetryEnabled { get; private set; }

    public bool AutomaticErrorReportsEnabled { get; private set; }

    public bool HasAcceptedCurrentPrivacyChoices =>
        _privacyGeneration >= 1
        && PrivacyChoicesVersion >= PrivacyChoicesPolicy.CurrentNoticeVersion;

    public bool CheckForUpdatesOnStartup { get; private set; }

    public bool IncludePrereleaseUpdates { get; private set; }

    public MacroLayoutProfile LayoutProfile { get; private set; }

    public MacroMinimizeBehavior MinimizeBehavior { get; private set; }

    public AppTheme ThemeMode { get; private set; }

    public AppColorTheme ColorTheme { get; private set; }

    public Dictionary<string, MacroLayoutProfile> RunnerLayoutProfiles { get; }

    public MacroMinimizeBehavior EffectiveMinimizeBehavior =>
        MacroDisplayPolicy.EffectiveMinimizeBehavior(LayoutProfile, MinimizeBehavior);

    public static async Task<MacroOwnerState> LoadAsync(
        MacroSettingsStore? settingsStore = null,
        ISecretProtector? secretProtector = null,
        CancellationToken cancellationToken = default)
    {
        MacroSettingsStore store = settingsStore ?? new MacroSettingsStore();
        MacroSettings settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        PersistedPrivacyChoices? privacy = await store.LoadPrivacyChoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        MacroKeyBindings keyBindings = new();
        keyBindings.ApplyPersisted(settings.KeyBindings);
        if (!PlanPersistence.TryRestore(settings.Plans, out ObservableCollection<PlanPrototype>? plans))
            plans = PlanPrototypeFactory.CreatePlans();
        return new MacroOwnerState(
            store,
            keyBindings,
            plans,
            settings.SelectedPlanIndex,
            secretProtector ?? new DpapiSecretProtector(MacroInstanceContext.Current.UsesMachineProtectedSecrets),
            settings,
            privacy?.Generation ?? 0);
    }

    public void SelectPlan(PlanPrototype plan)
    {
        int index = Plans.IndexOf(plan);
        if (index < 0) throw new InvalidOperationException("The selected plan is not owned by this session.");
        if (SelectedPlanIndex == index) return;
        SelectedPlanIndex = index;
        QueueSave();
        SelectedPlanChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyPlansChanged() => QueueSave();

    public void SetPrivateServerLink(string value)
    {
        value = value.Trim();
        if (string.Equals(value, PrivateServerLink, StringComparison.Ordinal)) return;
        _encryptedPrivateServerLink = _secretProtector.Protect(value);
        PrivateServerLink = value;
        QueueSave();
    }

    public void SetDiscordWebhook(string value)
    {
        value = value.Trim();
        if (string.Equals(value, DiscordWebhook, StringComparison.Ordinal)) return;
        _encryptedDiscordWebhook = _secretProtector.Protect(value);
        DiscordWebhook = value;
        QueueSave();
    }

    public void SetDiscordEventOptions(
        string userId,
        bool notifyOnRunStart,
        bool notifyOnRunStop,
        bool notifyOnTaskChange,
        bool notifyOnVictory,
        bool notifyOnDefeat,
        bool notifyOnRecovery,
        bool notifyOnTerminalFailure)
    {
        userId = userId.Trim();
        if (string.Equals(userId, DiscordUserId, StringComparison.Ordinal) &&
            notifyOnRunStart == NotifyOnRunStart &&
            notifyOnRunStop == NotifyOnRunStop &&
            notifyOnTaskChange == NotifyOnTaskChange &&
            notifyOnVictory == NotifyOnVictory &&
            notifyOnDefeat == NotifyOnDefeat &&
            notifyOnRecovery == NotifyOnRecovery &&
            notifyOnTerminalFailure == NotifyOnTerminalFailure)
        {
            return;
        }
        DiscordUserId = userId;
        NotifyOnRunStart = notifyOnRunStart;
        NotifyOnRunStop = notifyOnRunStop;
        NotifyOnTaskChange = notifyOnTaskChange;
        NotifyOnVictory = notifyOnVictory;
        NotifyOnDefeat = notifyOnDefeat;
        NotifyOnRecovery = notifyOnRecovery;
        NotifyOnTerminalFailure = notifyOnTerminalFailure;
        QueueSave();
    }

    public void SetDiagnosticUploadConsent(bool enabled)
    {
        if (enabled == EnableDiagnosticUploads) return;
        EnableDiagnosticUploads = enabled;
        QueueSave();
    }

    public async Task SavePrivacyChoicesAsync(
        bool onlineFeaturesEnabled,
        bool telemetryEnabled,
        bool automaticErrorReportsEnabled) =>
        await SavePrivacyChoicesCoreAsync(
            onlineFeaturesEnabled,
            telemetryEnabled,
            automaticErrorReportsEnabled);

    public Task SavePrivacyChoiceAsync(PrivacyChoiceKind kind, bool enabled) =>
        SavePrivacyChoicesCoreAsync(
            kind == PrivacyChoiceKind.OnlineFeatures ? enabled : null,
            kind == PrivacyChoiceKind.Telemetry ? enabled : null,
            kind == PrivacyChoiceKind.AutomaticErrorReports ? enabled : null);

    private async Task SavePrivacyChoicesCoreAsync(
        bool? onlineFeaturesEnabled,
        bool? telemetryEnabled,
        bool? automaticErrorReportsEnabled)
    {
        await _privacyCommitGate.WaitAsync();
        try
        {
            lock (_saveSync) _privacyCommitInProgress = true;
            bool desiredOnline = onlineFeaturesEnabled ?? OnlineFeaturesEnabled;
            bool desiredTelemetry = telemetryEnabled ?? TelemetryEnabled;
            bool desiredReports = automaticErrorReportsEnabled ?? AutomaticErrorReportsEnabled;

            bool revoked = RevokeDisabledChoices(
                desiredOnline,
                desiredTelemetry,
                desiredReports);
            if (revoked) PrivacyOptionsChanged?.Invoke(this, EventArgs.Empty);

            Task<PersistedPrivacyChoices> save;
            lock (_saveSync)
            {
                save = PersistPrivacyAfterAsync(
                    _pendingSave,
                    onlineFeaturesEnabled,
                    telemetryEnabled,
                    automaticErrorReportsEnabled);
                _pendingSave = save;
            }
            PersistedPrivacyChoices persisted = await save;

            _privacyGeneration = persisted.Generation;
            PrivacyChoicesVersion = persisted.NoticeVersion;
            OnlineFeaturesEnabled = persisted.OnlineFeaturesEnabled;
            TelemetryEnabled = persisted.TelemetryEnabled;
            AutomaticErrorReportsEnabled = persisted.AutomaticErrorReportsEnabled;
            PrivacyOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            bool queueDeferred;
            lock (_saveSync)
            {
                _privacyCommitInProgress = false;
                queueDeferred = _saveRequestedDuringPrivacyCommit;
                _saveRequestedDuringPrivacyCommit = false;
            }
            if (queueDeferred) QueueSave();
            _privacyCommitGate.Release();
        }
    }

    public void SetUpdateOptions(bool checkOnStartup, bool includePrerelease)
    {
        if (checkOnStartup == CheckForUpdatesOnStartup && includePrerelease == IncludePrereleaseUpdates) return;
        CheckForUpdatesOnStartup = checkOnStartup;
        IncludePrereleaseUpdates = includePrerelease;
        QueueSave();
    }

    public void SetDisplayOptions(MacroLayoutProfile layout, MacroMinimizeBehavior minimizeBehavior)
    {
        if (!Enum.IsDefined(layout) || !Enum.IsDefined(minimizeBehavior))
            throw new ArgumentOutOfRangeException(nameof(layout));
        if (layout == LayoutProfile && minimizeBehavior == MinimizeBehavior) return;
        LayoutProfile = layout;
        MinimizeBehavior = minimizeBehavior;
        QueueSave();
        DisplayOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAppearance(AppTheme mode, AppColorTheme colorTheme)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(colorTheme)) throw new ArgumentOutOfRangeException(nameof(colorTheme));
        if (mode == ThemeMode && colorTheme == ColorTheme) return;
        ThemeMode = mode;
        ColorTheme = colorTheme;
        QueueSave();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public MacroLayoutProfile RunnerLayoutProfile(string profileId) =>
        RunnerLayoutProfiles.GetValueOrDefault(profileId, MacroLayoutProfile.Full1920x1080);

    public void SetRunnerLayoutProfile(string profileId, MacroLayoutProfile layout)
    {
        if (!IsRunnerProfileId(profileId)) throw new ArgumentException("Runner profile identifier is invalid.", nameof(profileId));
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        if (RunnerLayoutProfile(profileId) == layout) return;
        RunnerLayoutProfiles[profileId] = layout;
        QueueSave();
    }

    public Task FlushAsync()
    {
        lock (_saveSync) return _pendingSave;
    }

    private void KeyBindings_OnChanged(object? sender, EventArgs eventArgs)
        => QueueSave();

    private void QueueSave()
    {
        lock (_saveSync)
        {
            if (_privacyCommitInProgress)
            {
                _saveRequestedDuringPrivacyCommit = true;
                return;
            }
            MacroSettings snapshot = CreateSnapshot();
            Task previous = _pendingSave;
            _pendingSave = PersistAfterAsync(previous, snapshot);
        }
    }

    private MacroSettings CreateSnapshot() => new()
    {
        KeyBindings = KeyBindings.CreatePersistedSnapshot(),
        Plans = PlanPersistence.CreateSnapshot(Plans),
        SelectedPlanIndex = SelectedPlanIndex,
        EncryptedPrivateServerLink = _encryptedPrivateServerLink,
        EncryptedDiscordWebhook = _encryptedDiscordWebhook,
        DiscordUserId = DiscordUserId,
        NotifyOnTerminalFailure = NotifyOnTerminalFailure,
        NotifyOnRunStart = NotifyOnRunStart,
        NotifyOnRunStop = NotifyOnRunStop,
        NotifyOnTaskChange = NotifyOnTaskChange,
        NotifyOnVictory = NotifyOnVictory,
        NotifyOnDefeat = NotifyOnDefeat,
        NotifyOnRecovery = NotifyOnRecovery,
        EnableDiagnosticUploads = EnableDiagnosticUploads,
        PrivacyChoicesVersion = PrivacyChoicesVersion,
        OnlineFeaturesEnabled = OnlineFeaturesEnabled,
        TelemetryEnabled = TelemetryEnabled,
        AutomaticErrorReportsEnabled = AutomaticErrorReportsEnabled,
        CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
        IncludePrereleaseUpdates = IncludePrereleaseUpdates,
        LayoutProfile = LayoutProfile,
        MinimizeBehavior = MinimizeBehavior,
        ThemeMode = ThemeMode,
        ColorTheme = ColorTheme,
        RunnerLayoutProfiles = new Dictionary<string, MacroLayoutProfile>(RunnerLayoutProfiles, StringComparer.Ordinal),
    };

    private bool RevokeDisabledChoices(
        bool onlineFeaturesEnabled,
        bool telemetryEnabled,
        bool automaticErrorReportsEnabled)
    {
        bool changed = false;
        if (!onlineFeaturesEnabled && OnlineFeaturesEnabled)
        {
            OnlineFeaturesEnabled = false;
            changed = true;
        }
        if (!telemetryEnabled && TelemetryEnabled)
        {
            TelemetryEnabled = false;
            changed = true;
        }
        if (!automaticErrorReportsEnabled && AutomaticErrorReportsEnabled)
        {
            AutomaticErrorReportsEnabled = false;
            changed = true;
        }
        return changed;
    }

    private async Task PersistAfterAsync(Task previous, MacroSettings snapshot)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A newer complete snapshot still gets an independent save attempt.
        }
        await _settingsStore.SaveAsync(snapshot).ConfigureAwait(false);
    }

    private async Task<PersistedPrivacyChoices> PersistPrivacyAfterAsync(
        Task previous,
        bool? onlineFeaturesEnabled,
        bool? telemetryEnabled,
        bool? automaticErrorReportsEnabled)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The complete privacy snapshot still gets independent bounded attempts.
        }
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await _settingsStore.SavePrivacyChoicesAsync(
                    onlineFeaturesEnabled,
                    telemetryEnabled,
                    automaticErrorReportsEnabled).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                attempt < 3 && exception is (IOException or UnauthorizedAccessException))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt)).ConfigureAwait(false);
            }
        }
    }

    public Task<bool> IsOnlineFeaturesDurablyEnabledAsync(CancellationToken cancellationToken = default) =>
        IsChoiceDurablyEnabledAsync(choice => choice.OnlineFeaturesEnabled, cancellationToken);

    public Task<bool> IsTelemetryDurablyEnabledAsync(CancellationToken cancellationToken = default) =>
        IsChoiceDurablyEnabledAsync(choice => choice.TelemetryEnabled, cancellationToken);

    public Task<bool> AreAutomaticReportsDurablyEnabledAsync(CancellationToken cancellationToken = default) =>
        IsChoiceDurablyEnabledAsync(choice => choice.AutomaticErrorReportsEnabled, cancellationToken);

    public bool IsTelemetryDurablyEnabled() =>
        IsChoiceDurablyEnabled(choice => choice.TelemetryEnabled);

    public bool AreAutomaticReportsDurablyEnabled() =>
        IsChoiceDurablyEnabled(choice => choice.AutomaticErrorReportsEnabled);

    private bool IsChoiceDurablyEnabled(Func<PersistedPrivacyChoices, bool> selector)
    {
        PersistedPrivacyChoices? persisted = _settingsStore.LoadPrivacyChoices();
        return persisted is not null
            && persisted.Generation == _privacyGeneration
            && persisted.NoticeVersion >= PrivacyChoicesPolicy.CurrentNoticeVersion
            && selector(persisted);
    }

    private async Task<bool> IsChoiceDurablyEnabledAsync(
        Func<PersistedPrivacyChoices, bool> selector,
        CancellationToken cancellationToken)
    {
        PersistedPrivacyChoices? persisted = await _settingsStore.LoadPrivacyChoicesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (persisted is null) return false;
        return persisted.Generation == _privacyGeneration
            && persisted.NoticeVersion >= PrivacyChoicesPolicy.CurrentNoticeVersion
            && selector(persisted);
    }

    private string UnprotectOrEmpty(string protectedValue, out bool valid)
    {
        try
        {
            valid = true;
            return _secretProtector.Unprotect(protectedValue);
        }
        catch (Exception exception) when (exception is InvalidDataException or Win32Exception)
        {
            valid = false;
            return string.Empty;
        }
    }

    private static bool IsRunnerProfileId(string value) =>
        value.Length is >= 1 and <= 32
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
