using LilacMacro.Core.Automation;
using LilacMacro.Core.Ocr;
using LilacMacro.App.Runtime;

namespace LilacMacro.Tests;

public sealed class EventRunPolicyTests
{
    [Theory]
    [InlineData(StoryAct.Act1, "event-villain-invasion-act-1", false)]
    [InlineData(StoryAct.Act2, "event-villain-invasion-act-2", false)]
    [InlineData(StoryAct.Act3, "event-villain-invasion-act-3", true)]
    [InlineData(StoryAct.Act4, "event-villain-invasion-act-4", true)]
    public void MapsSupportedActsAndScrollPolicy(
        StoryAct act,
        string expectedMapId,
        bool requiresScroll)
    {
        Assert.Equal(expectedMapId, EventRunPolicy.MapId(EventRunPolicy.VillainInvasion, act));
        Assert.Equal(requiresScroll, EventRunPolicy.RequiresActScroll(act));
    }

    [Theory]
    [InlineData("Boss Bounty", StoryAct.Act1)]
    [InlineData("Villain Invasion", StoryAct.Act5)]
    public void RejectsUnimplementedEventRoutes(string map, StoryAct act) =>
        Assert.Throws<InvalidDataException>(() => EventRunPolicy.MapId(map, act));

    [Fact]
    public void ActAliasesMatchObservedNames()
    {
        Assert.Contains("act1 death", EventRunPolicy.TargetFor(StoryAct.Act1).Aliases);
        Assert.Contains("crow dawn", EventRunPolicy.TargetFor(StoryAct.Act4).Aliases);
    }

    [Theory]
    [InlineData("Villain Invasion · Act 4")]
    [InlineData("Villain Invasion Â· Act 4")]
    public void PlanRouteParserToleratesPersistedSeparatorEncodings(string route)
    {
        (string map, StoryAct act) = MacroTaskOptionsFactory.ParseRoute(route);
        Assert.Equal("Villain Invasion", map);
        Assert.Equal(StoryAct.Act4, act);
    }
}
