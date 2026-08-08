namespace LilacMacro.Core.Automation;

public static class KeyboardKey
{
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftControl = 0xA2;
    public const int RightControl = 0xA3;

    public static bool IsSupportedAutomationKey(int virtualKey) => virtualKey switch
    {
        LeftShift or RightShift or LeftControl or RightControl => true,
        >= 0x21 and <= 0x28 => true,
        _ => IsSupportedGeneralKey(virtualKey),
    };

    public static string GetDisplayName(int virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }
        if (virtualKey is >= 0x60 and <= 0x69) return $"Num {virtualKey - 0x60}";
        if (virtualKey is >= 0x70 and <= 0x87) return $"F{virtualKey - 0x6F}";

        return virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x14 => "Caps Lock",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left Arrow",
            0x26 => "Up Arrow",
            0x27 => "Right Arrow",
            0x28 => "Down Arrow",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            0xE2 => "\\",
            LeftShift => "Left Shift",
            RightShift => "Right Shift",
            LeftControl => "Left Ctrl",
            RightControl => "Right Ctrl",
            _ => "Unsupported",
        };
    }

    private static bool IsSupportedGeneralKey(int virtualKey) => virtualKey switch
    {
        0x08 or 0x09 or 0x0D or 0x14 or 0x20 or 0x2D or 0x2E => true,
        >= 0x30 and <= 0x39 => true,
        >= 0x41 and <= 0x5A => true,
        >= 0x60 and <= 0x6B => true,
        >= 0x6D and <= 0x6F => true,
        >= 0x70 and <= 0x87 => true,
        >= 0xBA and <= 0xC0 => true,
        >= 0xDB and <= 0xDE => true,
        0xE2 => true,
        _ => false,
    };
}
