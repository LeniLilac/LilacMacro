using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class AutomationKeySequenceTests
{
    private const int F6VirtualKey = 0x75;

    [Theory]
    [InlineData(0x57, "W")]
    [InlineData(KeyboardKey.LeftShift, "Left Shift")]
    [InlineData(0x70, "F1")]
    [InlineData(0x6B, "Num +")]
    public void KeyPress_CreateAcceptsReferenceKeySet(int virtualKey, string displayName)
    {
        AutomationKeyPress keyPress = AutomationKeyPress.Create(virtualKey, 1000, F6VirtualKey);

        Assert.Equal(displayName, keyPress.KeyName);
    }

    [Theory]
    [InlineData(0x1B, 1000)]
    [InlineData(F6VirtualKey, 1000)]
    [InlineData(0x57, 0)]
    [InlineData(0x57, 120001)]
    public void KeyPress_CreateRejectsUnsupportedReservedOrUnboundedInput(
        int virtualKey,
        int holdMilliseconds)
    {
        Assert.Throws<InvalidDataException>(
            () => AutomationKeyPress.Create(virtualKey, holdMilliseconds, F6VirtualKey));
    }

    [Fact]
    public void Sequence_CopiesAndPreservesOrderedSteps()
    {
        List<AutomationKeyPress> source =
        [
            AutomationKeyPress.Create(0x57, 1000, F6VirtualKey),
            AutomationKeyPress.Create(0x44, 250, F6VirtualKey),
        ];

        AutomationKeySequence sequence = AutomationKeySequence.Create(source);
        source[0] = AutomationKeyPress.Create(0x41, 10, F6VirtualKey);

        Assert.Collection(
            sequence.Steps,
            step => Assert.Equal((0x57, 1000), (step.VirtualKey, step.HoldMilliseconds)),
            step => Assert.Equal((0x44, 250), (step.VirtualKey, step.HoldMilliseconds)));
        Assert.Equal(1250, sequence.TotalHoldMilliseconds);
    }

    [Fact]
    public void Sequence_RejectsEmptyOrOverlongChains()
    {
        Assert.Throws<InvalidDataException>(() => AutomationKeySequence.Create([]));
        AutomationKeyPress step = AutomationKeyPress.Create(0x57, 1, F6VirtualKey);
        Assert.Throws<InvalidDataException>(
            () => AutomationKeySequence.Create(Enumerable.Repeat(step, AutomationKeySequence.MaximumSteps + 1)));
    }

    [Fact]
    public void Sequence_RejectsExcessiveTotalHoldTime()
    {
        AutomationKeyPress step = AutomationKeyPress.Create(0x57, 120000, F6VirtualKey);

        Assert.Throws<InvalidDataException>(
            () => AutomationKeySequence.Create(Enumerable.Repeat(step, 6)));
    }
}
