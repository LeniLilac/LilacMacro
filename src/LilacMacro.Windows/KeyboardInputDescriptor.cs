using LilacMacro.Core.Automation;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

internal readonly record struct KeyboardInputDescriptor(int VirtualKey, int ScanCode, bool Extended)
{
    private const uint MapVirtualKeyToScanCodeExtended = 4;

    public static KeyboardInputDescriptor FromAutomationVirtualKey(int virtualKey)
    {
        if (!KeyboardKey.IsSupportedAutomationKey(virtualKey))
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), "The automation key is not supported.");
        }

        uint mapped = NativeInputMethods.MapVirtualKey((uint)virtualKey, MapVirtualKeyToScanCodeExtended);
        int scanCode = (int)(mapped & 0xFF);
        if (scanCode == 0)
        {
            throw new InvalidOperationException("Windows could not resolve the key to a physical scan code.");
        }
        bool extended =
            (mapped & 0xFF00) is 0xE000 or 0xE100 ||
            virtualKey is >= 0x21 and <= 0x28 or 0x2D or 0x2E;
        return new KeyboardInputDescriptor(virtualKey, scanCode, extended);
    }
}
