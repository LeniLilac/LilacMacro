using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;
using LilacMacro.Runtime.Normalization;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class CodeRedemptionService(
    WorkspaceController workspace,
    OcrRunner ocr,
    UtilityRespawnService respawn)
{
    private static readonly PixelSize ClientSize = DebugWorkflowCatalog.ClientSize;
    private static readonly PixelRect FullClient = new(0, 0, ClientSize.Width, ClientSize.Height);
    private static readonly TimeSpan ObservationDelay = TimeSpan.FromMilliseconds(300);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly ObservedStateTransitionRunner _transitions = new(workspace, ocr);

    public async Task RedeemAsync(
        string code,
        int? areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);
        _ = AutomationTextInput.Create(code, capsLockEnabled: false);

        DebugOcrSnapshot lobby = await _states.WaitForMatchAsync(
            DebugWorkflowCatalog.Lobby,
            device,
            8,
            ObservationDelay,
            cancellationToken).ConfigureAwait(false);
        if (!lobby.Evaluation.IsMatch)
            throw new InvalidOperationException("A verified Lobby is required before redeeming a code.");

        status($"CODE | OPENING | {code}");
        ObservedStateTransitionRunResult openLauncher = await _transitions.RunAsync(
            DebugWorkflowCatalog.Lobby,
            DebugCodeWorkflowCatalog.Launcher,
            device,
            OpenLauncherAsync,
            cancellationToken).ConfigureAwait(false);
        RequireTransition(openLauncher, "Lobby to code launcher");

        ObservedStateTransitionRunResult openPanel = await _transitions.RunAsync(
            DebugCodeWorkflowCatalog.Launcher,
            DebugCodeWorkflowCatalog.Panel,
            device,
            token => OpenPanelAsync(device, token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(openPanel, "Code launcher to Codes panel");

        DebugOcrSnapshot panel = openPanel.Observation.Destination;
        OcrTargetMatch input = RequireExact(
            DebugCodeWorkflowCatalog.Input,
            panel,
            "The verified Codes panel did not expose its empty code field.");
        await workspace.ClickRobloxAsync(
            ClientSize,
            input.Region.Bounds.Center,
            cancellationToken).ConfigureAwait(false);
        await workspace.RunTextInputAsync(ClientSize, code, cancellationToken).ConfigureAwait(false);

        for (int attempt = 1; attempt <= CodeRedemptionPolicy.RedeemAttempts; attempt++)
        {
            panel = await RequirePanelAsync(device, cancellationToken).ConfigureAwait(false);
            OcrTargetMatch redeem = RequireExact(
                DebugCodeWorkflowCatalog.Redeem,
                panel,
                "The verified Codes panel did not expose its Redeem Code action.");
            await workspace.ClickRobloxAsync(
                ClientSize,
                redeem.Region.Bounds.Center,
                cancellationToken).ConfigureAwait(false);
            status($"CODE | REDEEM {attempt}/{CodeRedemptionPolicy.RedeemAttempts} | {code}");
            if (attempt < CodeRedemptionPolicy.RedeemAttempts)
                await Task.Delay(CodeRedemptionPolicy.RedeemAttemptDelay, cancellationToken).ConfigureAwait(false);
        }

        status($"CODE | CLEANUP | {code}");
        await respawn.RunAsync(
            areasMenuVirtualKey,
            reservedVirtualKey,
            device,
            cancellationToken).ConfigureAwait(false);
        status($"CODE | COMPLETE | {code} | LOBBY VERIFIED");
    }

    private async Task<ObservedStateTransitionActionResult> OpenLauncherAsync(
        CancellationToken cancellationToken)
    {
        RgbImage image = (await workspace.CaptureRgbRegionsAsync(
            ClientSize,
            [FullClient],
            cancellationToken).ConfigureAwait(false)).Single().Image;
        PixelPoint? gear = UiScalePanelDetector.DetectSettingsGear(image);
        if (gear is null)
            return new(false, "SETTINGS GEAR NOT VERIFIED", []);
        PixelPoint launcherPoint = CodeRedemptionPolicy.LauncherPoint(gear.Value, ClientSize);
        await workspace.ClickRobloxAsync(ClientSize, launcherPoint, cancellationToken).ConfigureAwait(false);
        return new(true, "CODE LAUNCHER CLICKED", ["SETTINGS GEAR VERIFIED + LAUNCHER CLICKED"]);
    }

    private async Task<ObservedStateTransitionActionResult> OpenPanelAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot launcher = await _states.RunAsync(
            DebugCodeWorkflowCatalog.Launcher,
            device,
            cancellationToken).ConfigureAwait(false);
        if (!launcher.Evaluation.IsMatch)
            return new(false, "CODE LAUNCHER NOT VERIFIED", []);
        OcrTargetMatch redeemCodes = RequireExact(
            DebugCodeWorkflowCatalog.Launcher.Targets[1],
            launcher,
            "The verified launcher did not expose Redeem Codes.");
        await workspace.ClickRobloxAsync(
            ClientSize,
            redeemCodes.Region.Bounds.Center,
            cancellationToken).ConfigureAwait(false);
        return new(true, "REDEEM CODES CLICKED", ["CODE LAUNCHER VERIFIED + REDEEM CODES CLICKED"]);
    }

    private async Task<DebugOcrSnapshot> RequirePanelAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot panel = await _states.WaitForMatchAsync(
            DebugCodeWorkflowCatalog.Panel,
            device,
            8,
            ObservationDelay,
            cancellationToken).ConfigureAwait(false);
        return panel.Evaluation.IsMatch
            ? panel
            : throw new InvalidOperationException("Codes panel was not freshly verified.");
    }

    private static OcrTargetMatch RequireExact(
        OcrTargetRule target,
        DebugOcrSnapshot snapshot,
        string error) => OcrRuleEngine.FindExactTarget(target, snapshot.Regions)
        ?? throw new InvalidOperationException(error);

    private static void RequireTransition(
        ObservedStateTransitionRunResult transition,
        string name)
    {
        if (!transition.Succeeded)
            throw new InvalidOperationException($"{name} transition was not verified.");
    }
}
