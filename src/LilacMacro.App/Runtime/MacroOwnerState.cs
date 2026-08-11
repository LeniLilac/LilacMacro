using System.Collections.ObjectModel;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Views;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.App.Runtime;

internal sealed class MacroOwnerState
{
    private readonly MacroSettingsStore _settingsStore;
    private readonly object _saveSync = new();
    private Task _pendingSave = Task.CompletedTask;

    private MacroOwnerState(
        MacroSettingsStore settingsStore,
        MacroKeyBindings keyBindings,
        ExecutionTarget executionTarget)
    {
        _settingsStore = settingsStore;
        KeyBindings = keyBindings;
        ExecutionTarget = executionTarget;
        KeyBindings.Changed += KeyBindings_OnChanged;
    }

    public ObservableCollection<PlanPrototype> Plans { get; } = PlanPrototypeFactory.CreatePlans();

    public MacroKeyBindings KeyBindings { get; }

    public ExecutionTarget ExecutionTarget { get; private set; }

    public string PrivateServerLink { get; set; } = string.Empty;

    public static async Task<MacroOwnerState> LoadAsync(
        MacroSettingsStore? settingsStore = null,
        CancellationToken cancellationToken = default)
    {
        MacroSettingsStore store = settingsStore ?? new MacroSettingsStore();
        MacroSettings settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        MacroKeyBindings keyBindings = new();
        keyBindings.ApplyPersisted(settings.KeyBindings);
        return new MacroOwnerState(store, keyBindings, settings.ExecutionTarget);
    }

    public void SetExecutionTarget(ExecutionTarget target)
    {
        if (ExecutionTarget == target) return;
        ExecutionTarget = target;
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
            ExecutionTarget = ExecutionTarget,
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
}
