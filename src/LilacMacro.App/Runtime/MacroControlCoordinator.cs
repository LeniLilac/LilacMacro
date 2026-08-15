using LilacMacro.App.Views;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.App.Runtime;

internal sealed class MacroControlCoordinator(
    ControlSnapshotPollingService polling,
    Func<bool> onlineFeaturesEnabled)
{
    private readonly ControlSnapshotPollingService _polling =
        polling ?? throw new ArgumentNullException(nameof(polling));
    private readonly Func<bool> _onlineFeaturesEnabled =
        onlineFeaturesEnabled ?? throw new ArgumentNullException(nameof(onlineFeaturesEnabled));

    private SignedControlSnapshot? Current =>
        _onlineFeaturesEnabled() ? _polling.Current : null;

    public DateTimeOffset SnapshotExpiry =>
        Current?.Payload.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(1);

    public bool CanStart(out string message)
    {
        SignedControlSnapshot? snapshot = Current;
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
        MacroControlPolicy.IsTaskEnabled(Current, task, now);

    public bool IsTeamSwapEnabled(DateTimeOffset now) =>
        MacroControlPolicy.IsTeamSwapEnabled(Current, now);

    public bool IsSettingsNormalizerEnabled(DateTimeOffset now) =>
        MacroControlPolicy.IsSettingsNormalizerEnabled(Current, now);

    public bool IsCodeRedemptionEnabled(DateTimeOffset now) =>
        ControlOperationalPolicy.IsFeatureEnabled(
            Current,
            "task.code-redeem",
            now);

    public IReadOnlyList<string> ActiveCodes(DateTimeOffset now) =>
        ControlOperationalPolicy.ActiveCodes(Current, now);

    public DateTimeOffset NextUtilityDue(
        PlanTaskPrototype task,
        DateTimeOffset completedAt) => MacroControlPolicy.NextUtilityDue(
            Current,
            task,
            completedAt);
}
