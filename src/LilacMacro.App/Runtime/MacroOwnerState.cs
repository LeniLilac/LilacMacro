using System.Collections.ObjectModel;
using System.ComponentModel;
using LilacMacro.App.Infrastructure;
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
    private Task _pendingSave = Task.CompletedTask;
    private string _encryptedPrivateServerLink;
    private string _encryptedDiscordWebhook;

    private MacroOwnerState(
        MacroSettingsStore settingsStore,
        MacroKeyBindings keyBindings,
        ObservableCollection<PlanPrototype> plans,
        int selectedPlanIndex,
        ISecretProtector secretProtector,
        MacroSettings settings)
    {
        _settingsStore = settingsStore;
        _secretProtector = secretProtector;
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
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        IncludePrereleaseUpdates = settings.IncludePrereleaseUpdates;
        LayoutProfile = Enum.IsDefined(settings.LayoutProfile)
            ? settings.LayoutProfile
            : MacroLayoutProfile.Full1920x1080;
        MinimizeBehavior = Enum.IsDefined(settings.MinimizeBehavior)
            ? settings.MinimizeBehavior
            : MacroMinimizeBehavior.WhileRunning;
        RunnerLayoutProfiles = (settings.RunnerLayoutProfiles ?? [])
            .Where(item => IsRunnerProfileId(item.Key) && Enum.IsDefined(item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        KeyBindings.Changed += KeyBindings_OnChanged;
    }

    public event EventHandler? SelectedPlanChanged;

    public event EventHandler? DisplayOptionsChanged;

    public ObservableCollection<PlanPrototype> Plans { get; }

    public int SelectedPlanIndex { get; private set; }

    public PlanPrototype SelectedPlan => Plans[SelectedPlanIndex];

    public MacroKeyBindings KeyBindings { get; }

    public ExecutionTarget ExecutionTarget => ExecutionTarget.LocalDesktop;

    public string PrivateServerLink { get; private set; }

    public string DiscordWebhook { get; private set; }

    public string DiscordUserId { get; private set; }

    public bool NotifyOnTerminalFailure { get; private set; }

    public bool CheckForUpdatesOnStartup { get; private set; }

    public bool IncludePrereleaseUpdates { get; private set; }

    public MacroLayoutProfile LayoutProfile { get; private set; }

    public MacroMinimizeBehavior MinimizeBehavior { get; private set; }

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
            settings);
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

    public void SetDiscordFailureOptions(string userId, bool notifyOnTerminalFailure)
    {
        userId = userId.Trim();
        if (string.Equals(userId, DiscordUserId, StringComparison.Ordinal) &&
            notifyOnTerminalFailure == NotifyOnTerminalFailure)
        {
            return;
        }
        DiscordUserId = userId;
        NotifyOnTerminalFailure = notifyOnTerminalFailure;
        QueueSave();
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
        MacroSettings snapshot = new()
        {
            KeyBindings = KeyBindings.CreatePersistedSnapshot(),
            Plans = PlanPersistence.CreateSnapshot(Plans),
            SelectedPlanIndex = SelectedPlanIndex,
            EncryptedPrivateServerLink = _encryptedPrivateServerLink,
            EncryptedDiscordWebhook = _encryptedDiscordWebhook,
            DiscordUserId = DiscordUserId,
            NotifyOnTerminalFailure = NotifyOnTerminalFailure,
            CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
            IncludePrereleaseUpdates = IncludePrereleaseUpdates,
            LayoutProfile = LayoutProfile,
            MinimizeBehavior = MinimizeBehavior,
            RunnerLayoutProfiles = new Dictionary<string, MacroLayoutProfile>(RunnerLayoutProfiles, StringComparer.Ordinal),
        };
        lock (_saveSync)
        {
            Task previous = _pendingSave;
            _pendingSave = PersistAfterAsync(previous, snapshot);
        }
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
