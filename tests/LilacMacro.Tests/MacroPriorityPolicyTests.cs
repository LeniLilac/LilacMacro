using LilacMacro.App.Runtime;
using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class MacroPriorityPolicyTests
{
    [Fact]
    public void SelectsLowestIncompletePriorityAcrossLoops()
    {
        PlanTaskPrototype story = Task(PlanTaskMode.Story, 2, 2);
        PlanTaskPrototype raid = Task(PlanTaskMode.Raid, 1, 1);
        PlanLoopPrototype loop = new();
        loop.Children.Add(raid);
        PlanPrototype plan = new("test", [story, loop]);
        Dictionary<PlanTaskPrototype, int> victories = [];

        Assert.Same(raid, MacroPriorityPolicy.Select(plan, victories));
        victories[raid] = 1;
        Assert.Same(story, MacroPriorityPolicy.Select(plan, victories));
        victories[story] = 2;
        Assert.Null(MacroPriorityPolicy.Select(plan, victories));
    }

    [Fact]
    public void UnsupportedHighestPriorityIsNotSilentlySkipped()
    {
        PlanTaskPrototype limitedEvent = Task(PlanTaskMode.Event, 1, 1);
        PlanTaskPrototype story = Task(PlanTaskMode.Story, 2, 1);
        PlanPrototype plan = new("test", [limitedEvent, story]);

        PlanTaskPrototype selected = Assert.IsType<PlanTaskPrototype>(
            MacroPriorityPolicy.Select(plan, new Dictionary<PlanTaskPrototype, int>()));

        Assert.Same(limitedEvent, selected);
        Assert.False(MacroPriorityPolicy.Supported(selected));
    }

    [Fact]
    public void ChallengeRemainsPendingButCanBeTemporarilyIneligible()
    {
        PlanTaskPrototype challenge = Task(PlanTaskMode.Challenge, 1, 1);
        PlanTaskPrototype story = Task(PlanTaskMode.Story, 2, 1);
        PlanPrototype plan = new("test", [challenge, story]);
        Dictionary<PlanTaskPrototype, int> victories = new() { [challenge] = 12 };

        Assert.Same(challenge, MacroPriorityPolicy.Select(plan, victories));
        Assert.Same(story, MacroPriorityPolicy.Select(plan, victories, task => task != challenge));
        Assert.True(MacroPriorityPolicy.Supported(challenge));
    }

    [Theory]
    [InlineData("https://www.roblox.com/share?code=secret&type=Server")]
    [InlineData("https://roblox.com/games/1?privateServerLinkCode=secret")]
    public void ConvertsPrivateServerLinksToRobloxProtocol(string value) =>
        Assert.Equal("roblox", PrivateServerRejoinService.Validate(value).LaunchUri.Scheme);

    [Theory]
    [InlineData("")]
    [InlineData("roblox://placeId=1")]
    [InlineData("https://roblox.com/games/1")]
    [InlineData("https://example.com/share?code=secret")]
    public void RejectsUnsafePrivateServerLinks(string value) =>
        Assert.Throws<InvalidOperationException>(() => PrivateServerRejoinService.Validate(value));

    private static PlanTaskPrototype Task(PlanTaskMode mode, int priority, int target) => new()
    {
        Mode = mode,
        Priority = priority,
        Target = target,
    };
}
