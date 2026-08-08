using LilacMacro.Core.Automation;
using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class KeyboardInputDescriptorTests
{
    [Fact]
    public void LetterKey_UsesPhysicalNonExtendedScanCode()
    {
        KeyboardInputDescriptor key = KeyboardInputDescriptor.FromAutomationVirtualKey(0x57);

        Assert.Equal(0x57, key.VirtualKey);
        Assert.True(key.ScanCode > 0);
        Assert.False(key.Extended);
    }

    [Fact]
    public void NavigationKey_IsExtended()
    {
        KeyboardInputDescriptor key = KeyboardInputDescriptor.FromAutomationVirtualKey(0x28);

        Assert.True(key.ScanCode > 0);
        Assert.True(key.Extended);
    }

    [Fact]
    public void UnsupportedKey_IsRejectedBeforeNativeMapping()
    {
        Assert.False(KeyboardKey.IsSupportedAutomationKey(0x1B));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KeyboardInputDescriptor.FromAutomationVirtualKey(0x1B));
    }
}
