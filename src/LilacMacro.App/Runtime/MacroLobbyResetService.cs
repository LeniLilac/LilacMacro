using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Views;
using LilacMacro.App.Workspace;
using LilacMacro.Runtime.Normalization;

namespace LilacMacro.App.Runtime;

internal sealed class MacroLobbyResetService
{
    private readonly MacroOwnerState _ownerState;
    private readonly MacroControlCoordinator _control;
    private readonly PrivateServerRejoinService _rejoin;
    private readonly UiScaleNormalizer _uiScale;
    private readonly GameSettingsNormalizer _gameSettings;
    private readonly CodeRedemptionService _codes;
    private readonly Action<string> _status;
    private readonly Action _robloxRejoined;

    public MacroLobbyResetService(
        MacroOwnerState ownerState,
        MacroControlCoordinator control,
        WorkspaceController workspace,
        OcrRunner ocr,
        DeepDebugSessionService deepDebug,
        Action<string> status,
        Action robloxRejoined)
    {
        _ownerState = ownerState;
        _control = control;
        _rejoin = new PrivateServerRejoinService(workspace, ocr);
        _uiScale = new UiScaleNormalizer(workspace, ocr, deepDebug);
        _gameSettings = new GameSettingsNormalizer(workspace, deepDebug);
        _codes = new CodeRedemptionService(
            workspace,
            ocr,
            new UtilityRespawnService(workspace, ocr));
        _status = status;
        _robloxRejoined = robloxRejoined;
    }

    public bool HasPendingCodes(IReadOnlySet<string> redeemedCodes, DateTimeOffset now) =>
        _control.IsCodeRedemptionEnabled(now) &&
        _control.ActiveCodes(now).Any(code => !redeemedCodes.Contains(code));

    public async Task ResetAsync(
        string device,
        bool normalizeStartupSettings,
        HashSet<string> redeemedCodes,
        CancellationToken cancellationToken)
    {
        await _rejoin.RejoinAndVerifyLobbyAsync(
            _ownerState.PrivateServerLink,
            device,
            _status,
            cancellationToken).ConfigureAwait(false);
        _robloxRejoined();
        if (normalizeStartupSettings &&
            _control.IsSettingsNormalizerEnabled(DateTimeOffset.UtcNow))
        {
            await _uiScale.NormalizeAsync(device, _status, cancellationToken).ConfigureAwait(false);
            await _gameSettings.NormalizeAsync(_status, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!_control.IsCodeRedemptionEnabled(now)) return;
        MacroRuntimeKeySnapshot keys = _ownerState.KeyBindings.Snapshot();
        foreach (string code in _control.ActiveCodes(now).Where(code => !redeemedCodes.Contains(code)))
        {
            await _codes.RedeemAsync(
                code,
                keys.AreasMenu,
                keys.MacroToggle,
                device,
                _status,
                cancellationToken).ConfigureAwait(false);
            _ = redeemedCodes.Add(code);
        }
    }
}
