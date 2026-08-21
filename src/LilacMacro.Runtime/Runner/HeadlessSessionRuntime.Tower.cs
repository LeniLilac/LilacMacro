using LilacMacro.App.Debugging;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;
using LilacMacro.Core.LocalSession;
using LilacMacro.Runtime.Normalization;

namespace LilacMacro.Runtime.Runner;

public sealed partial class HeadlessSessionRuntime
{
    private static async Task CompleteAlreadyClearedTowerGoalAsync(
        StoryWireTestResult result,
        StoryWireTestOptions options,
        RunnerTaskSnapshot task,
        Dictionary<string, int> wins,
        Dictionary<string, int> losses,
        IProgress<SessionRuntimeProgress> progress,
        PrivateServerRejoinService rejoin,
        UiScaleNormalizer uiScale,
        GameSettingsNormalizer gameSettings,
        string privateServerLink,
        string device,
        CancellationToken cancellationToken)
    {
        MatchTaskProgressPolicy.ApplyObservedTowerAvailability(
            task.Id, options.TowerFloor, wins, losses);
        Report(progress, task, "tower-goal-already-cleared", wins, losses, result.Status);
        await ResetLobbyAsync(
            rejoin,
            uiScale,
            gameSettings,
            privateServerLink,
            device,
            normalizeStartupSettings: false,
            detail => Report(progress, task, "lobby-reset", wins, losses, detail),
            cancellationToken).ConfigureAwait(false);
    }
}
