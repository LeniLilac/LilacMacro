using LilacMacro.App.Views;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.App.Runtime;

internal sealed class MacroControlCoordinator(ControlSnapshotPollingService polling)
{
    private readonly ControlSnapshotPollingService _polling =
        polling ?? throw new ArgumentNullException(nameof(polling));

    public DateTimeOffset SnapshotExpiry =>
        _polling.Current?.Payload.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(1);

    public bool CanStart(out string message)
    {
        SignedControlSnapshot? snapshot = _polling.Current;
        if (ControlOperationalPolicy.IsGameAvailable(snapshot))
        {
            message = string.Empty;
            return true;
        }
        message = ControlOperationalPolicy.GameUnavailableMessage(snapshot!);
        return false;
    }

    public bool CanContinue(Action<string> appendLog)
    {
        ArgumentNullException.ThrowIfNull(appendLog);
        if (CanStart(out string message)) return true;
        appendLog($"SAFE STOP | {message}");
        return false;
    }

    public bool IsTaskEnabled(PlanTaskPrototype task, DateTimeOffset now) =>
        MacroControlPolicy.IsTaskEnabled(_polling.Current, task, now);

    public bool IsTeamSwapEnabled(DateTimeOffset now) =>
        MacroControlPolicy.IsTeamSwapEnabled(_polling.Current, now);

    public bool IsSettingsNormalizerEnabled(DateTimeOffset now) =>
        MacroControlPolicy.IsSettingsNormalizerEnabled(_polling.Current, now);

    public bool IsCodeRedemptionEnabled(DateTimeOffset now) =>
        ControlOperationalPolicy.IsFeatureEnabled(
            _polling.Current,
            "task.code-redeem",
            now);

    public IReadOnlyList<string> ActiveCodes(DateTimeOffset now) =>
        ControlOperationalPolicy.ActiveCodes(_polling.Current, now);

    public DateTimeOffset NextUtilityDue(
        PlanTaskPrototype task,
        DateTimeOffset completedAt) => MacroControlPolicy.NextUtilityDue(
            _polling.Current,
            task,
            completedAt);
}
