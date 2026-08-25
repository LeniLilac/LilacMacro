using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionRewardPopupService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IncompleteChoiceDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SelectionDelay = TimeSpan.FromSeconds(3);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<bool> DismissAllAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        bool handled = false;
        int selections = 0;
        for (int observation = 1;
             observation <= ExpeditionRewardPopupPolicy.MaximumObservationAttempts;
             observation++)
        {
            DebugOcrSnapshot observed = await _states.RunAsync(
                ExpeditionRewardStateCatalog.Popup,
                device,
                cancellationToken).ConfigureAwait(false);
            if (!ExpeditionRewardPopupPolicy.HasBlockingEvidence(observed.Regions)) return handled;

            handled = true;
            if (!ExpeditionRewardPopupPolicy.IsPopup(observed.Regions))
            {
                status?.Invoke(
                    $"EXPEDITION REWARD POPUP SETTLING " +
                    $"{observation}/{ExpeditionRewardPopupPolicy.MaximumObservationAttempts}");
                await Task.Delay(IncompleteChoiceDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (selections >= ExpeditionRewardPopupPolicy.MaximumConsecutivePopups)
                break;

            status?.Invoke(
                $"EXPEDITION REWARD POPUP DETECTED; WAITING {SettleDelay.TotalSeconds:0}S " +
                $"FOR FRESH SELECTION {selections + 1}/{ExpeditionRewardPopupPolicy.MaximumConsecutivePopups}");
            await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);

            DebugOcrSnapshot settled = await _states.RunAsync(
                ExpeditionRewardStateCatalog.Popup,
                device,
                cancellationToken).ConfigureAwait(false);
            if (!ExpeditionRewardPopupPolicy.IsPopup(settled.Regions))
            {
                status?.Invoke("EXPEDITION REWARD POPUP CHANGED DURING SETTLE");
                continue;
            }

            OcrTextRegion? target = ExpeditionRewardPopupPolicy.SelectRightmost(settled.Regions);
            if (target is null)
            {
                throw new InvalidOperationException(
                    "Expedition reward popup did not expose a unique rightmost Select Upgrade target.");
            }

            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                target.Bounds.Center,
                cancellationToken).ConfigureAwait(false);
            status?.Invoke("EXPEDITION REWARD RIGHTMOST SELECT UPGRADE CLICKED");
            selections++;
            await Task.Delay(SelectionDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Expedition reward popup did not clear within its bounded observation window.");
    }
}
