using LilacMacro.Core.Automation;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

internal static class RobloxKeyboardInput
{
    private const int CapsLockVirtualKey = 0x14;
    private const int TextKeyHoldMilliseconds = 35;
    private const int TextKeyGapMilliseconds = 20;

    public static async Task SendTextAsync(
        string value,
        CancellationToken cancellationToken)
    {
        bool capsLockEnabled = (NativeInputMethods.GetKeyState(CapsLockVirtualKey) & 1) != 0;
        IReadOnlyList<AutomationTextStroke> strokes = AutomationTextInput.Create(
            value,
            capsLockEnabled);
        foreach (AutomationTextStroke stroke in strokes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyboardInputDescriptor key = KeyboardInputDescriptor.FromAutomationVirtualKey(stroke.VirtualKey);
            KeyboardInputDescriptor? shift = stroke.Shift
                ? KeyboardInputDescriptor.FromAutomationVirtualKey(KeyboardKey.LeftShift)
                : null;
            if (shift is { } modifier) SendKey(modifier, keyUp: false);
            try
            {
                SendKey(key, keyUp: false);
                try
                {
                    await Task.Delay(TextKeyHoldMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    SendKey(key, keyUp: true);
                }
            }
            finally
            {
                if (shift is { } releaseModifier) SendKey(releaseModifier, keyUp: true);
            }
            await Task.Delay(TextKeyGapMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task HoldKeyAsync(
        AutomationKeyPress keyPress,
        CancellationToken cancellationToken)
    {
        KeyboardInputDescriptor key = KeyboardInputDescriptor.FromAutomationVirtualKey(keyPress.VirtualKey);
        SendKey(key, keyUp: false);
        try
        {
            await Task.Delay(keyPress.HoldMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SendKey(key, keyUp: true);
        }
    }

    public static Task TapKeyAsync(int virtualKey, CancellationToken cancellationToken) =>
        TapKeyAsync(virtualKey, RobloxInputProtocol.ShiftLockKeyHoldMilliseconds, cancellationToken);

    public static Task TapKeyAsync(
        int virtualKey,
        int holdMilliseconds,
        CancellationToken cancellationToken) => HoldKeyAsync(
        new AutomationKeyPress(virtualKey, holdMilliseconds),
        cancellationToken);

    public static void SendKey(KeyboardInputDescriptor key, bool keyUp)
    {
        uint flags = key.Extended ? NativeInputMethods.KeyExtended : 0;
        if (keyUp) flags |= NativeInputMethods.KeyUp;
        NativeInputMethods.keybd_event((byte)key.VirtualKey, (byte)key.ScanCode, flags, 0);
    }
}
