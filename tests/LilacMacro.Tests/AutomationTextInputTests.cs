using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class AutomationTextInputTests
{
    [Fact]
    public void Create_PreservesCaseAndSupportedPunctuation()
    {
        AutomationTextStroke[] strokes = AutomationTextInput.Create("Ab9-_", capsLockEnabled: false).ToArray();

        Assert.Equal(
            [
                new AutomationTextStroke('A', Shift: true),
                new AutomationTextStroke('B', Shift: false),
                new AutomationTextStroke('9', Shift: false),
                new AutomationTextStroke(AutomationTextInput.OemMinusVirtualKey, Shift: false),
                new AutomationTextStroke(AutomationTextInput.OemMinusVirtualKey, Shift: true),
            ],
            strokes);
    }

    [Fact]
    public void Create_CompensatesForCapsLock()
    {
        AutomationTextStroke[] strokes = AutomationTextInput.Create("Ab", capsLockEnabled: true).ToArray();

        Assert.Equal(
            [
                new AutomationTextStroke('A', Shift: false),
                new AutomationTextStroke('B', Shift: true),
            ],
            strokes);
    }

    [Fact]
    public void Create_RejectsEmptyText()
    {
        Assert.Throws<ArgumentException>(() => AutomationTextInput.Create(string.Empty, capsLockEnabled: false));
    }

    [Theory]
    [InlineData("bad code")]
    [InlineData("bad!")]
    public void Create_RejectsUnsafeCharacters(string value)
    {
        Assert.Throws<InvalidDataException>(() => AutomationTextInput.Create(value, capsLockEnabled: false));
    }

    [Fact]
    public void Create_RejectsOverlongText()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomationTextInput.Create(
            new string('a', AutomationTextInput.MaximumLength + 1),
            capsLockEnabled: false));
    }
}
