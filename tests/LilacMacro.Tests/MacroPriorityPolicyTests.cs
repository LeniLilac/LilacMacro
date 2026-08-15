using LilacMacro.App.Runtime;
using LilacMacro.App.Views;
using LilacMacro.Core.Automation;

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
    public void EventParticipatesInPrioritySelection()
    {
        PlanTaskPrototype limitedEvent = Task(PlanTaskMode.Event, 1, 1);
        PlanTaskPrototype story = Task(PlanTaskMode.Story, 2, 1);
        PlanPrototype plan = new("test", [limitedEvent, story]);

        PlanTaskPrototype selected = Assert.IsType<PlanTaskPrototype>(
            MacroPriorityPolicy.Select(plan, new Dictionary<PlanTaskPrototype, int>()));

        Assert.Same(limitedEvent, selected);
        Assert.True(MacroPriorityPolicy.Supported(selected));
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

    [Fact]
    public void UtilityRemainsPendingAndHandsOffWhileTemporarilyIneligible()
    {
        PlanTaskPrototype utility = Task(PlanTaskMode.Utilities, 1, 400);
        utility.Route = ResourceRefuelPolicy.GoldMineRoute;
        PlanTaskPrototype raid = Task(PlanTaskMode.Raid, 2, 1);
        PlanPrototype plan = new("test", [utility, raid]);
        Dictionary<PlanTaskPrototype, int> victories = [];

        Assert.Same(utility, MacroPriorityPolicy.Select(plan, victories));
        Assert.Same(raid, MacroPriorityPolicy.Select(plan, victories, task => task != utility));
        Assert.True(MacroPriorityPolicy.IsPending(utility, victories));
        Assert.True(MacroPriorityPolicy.Supported(utility));
    }

    [Fact]
    public void EligibleSelectionUsesOneSharedObservationTime()
    {
        PlanTaskPrototype expedition = Task(PlanTaskMode.Expedition, 1, 15_000);
        PlanPrototype plan = new("test", [expedition]);
        DateTimeOffset observedAt = new(2026, 8, 14, 21, 2, 30, TimeSpan.Zero);
        List<DateTimeOffset> observations = [];

        PlanTaskPrototype? selected = MacroPriorityPolicy.SelectEligibleAt(
            plan,
            new Dictionary<PlanTaskPrototype, int> { [expedition] = 2 },
            observedAt,
            (_, fallback) =>
            {
                observations.Add(fallback);
                return fallback;
            },
            (_, enabledAt) =>
            {
                observations.Add(enabledAt);
                return true;
            });

        Assert.Same(expedition, selected);
        Assert.Equal([observedAt, observedAt], observations);
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
