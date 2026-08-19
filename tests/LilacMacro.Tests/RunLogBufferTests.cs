using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class RunLogBufferTests
{
    [Fact]
    public void RetainsOnlyTheNewestThousandEntries()
    {
        RunLogBuffer buffer = new();
        for (int index = 0; index < 1_005; index++) buffer.Add($"event-{index}");

        Assert.True(buffer.TryGetUpdatedText(out string text));
        string[] entries = text.Split([Environment.NewLine], StringSplitOptions.None);

        Assert.Equal(1_000, entries.Length);
        Assert.Equal("event-5", entries[0]);
        Assert.Equal("event-1004", entries[^1]);
    }

    [Fact]
    public void DoesNotProduceAnotherPresentationWhenNothingChanged()
    {
        RunLogBuffer buffer = new(2);
        buffer.Add("first");

        Assert.True(buffer.TryGetUpdatedText(out string firstText));
        Assert.Equal("first", firstText);
        Assert.False(buffer.TryGetUpdatedText(out _));

        buffer.Add("second");
        Assert.True(buffer.TryGetUpdatedText(out string secondText));
        Assert.Equal("first" + Environment.NewLine + "second", secondText);
    }

    [Fact]
    public void RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunLogBuffer(0));
    }
}
