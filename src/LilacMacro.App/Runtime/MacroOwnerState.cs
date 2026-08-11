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
        IncludeFailureDetails = settings.IncludeFailureDetails;
        KeyBindings.Changed += KeyBindings_OnChanged;
    }

    public event EventHandler? SelectedPlanChanged;

    public ObservableCollection<PlanPrototype> Plans { get; }

    public int SelectedPlanIndex { get; private set; }

    public PlanPrototype SelectedPlan => Plans[SelectedPlanIndex];

    public MacroKeyBindings KeyBindings { get; }

    public ExecutionTarget ExecutionTarget => ExecutionTarget.LocalDesktop;

    public string PrivateServerLink { get; private set; }

    public string DiscordWebhook { get; private set; }

    public string DiscordUserId { get; private set; }

    public bool NotifyOnTerminalFailure { get; private set; }

    public bool IncludeFailureDetails { get; private set; }

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

    public void SetDiscordFailureOptions(string userId, bool notifyOnTerminalFailure, bool includeFailureDetails)
    {
        userId = userId.Trim();
        if (string.Equals(userId, DiscordUserId, StringComparison.Ordinal) &&
            notifyOnTerminalFailure == NotifyOnTerminalFailure &&
            includeFailureDetails == IncludeFailureDetails)
        {
            return;
        }
        DiscordUserId = userId;
        NotifyOnTerminalFailure = notifyOnTerminalFailure;
        IncludeFailureDetails = includeFailureDetails;
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
            IncludeFailureDetails = IncludeFailureDetails,
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
}
