using LilacMacro.App.Debugging;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class RaidDropDismissalPolicyTests
{
    [Theory]
    [InlineData(StoryAct.Act2)]
    [InlineData(StoryAct.Act3)]
    public void EnablesOnlyObservedRaidActs(StoryAct act) =>
        Assert.True(RaidDropDismissalPolicy.IsEnabled(WireGameMode.Raid, act));

    [Theory]
    [InlineData((int)WireGameMode.Raid, StoryAct.Act1)]
    [InlineData((int)WireGameMode.Raid, StoryAct.Act4)]
    [InlineData((int)WireGameMode.Story, StoryAct.Act2)]
    [InlineData((int)WireGameMode.Challenge, StoryAct.Act3)]
    public void LeavesOtherRoutesUntouched(int mode, StoryAct act) =>
        Assert.False(RaidDropDismissalPolicy.IsEnabled((WireGameMode)mode, act));

    [Fact]
    public void UsesBoundedCanonicalClientPoint() =>
        Assert.Equal(new PixelPoint(1341, 675), RaidDropDismissalPolicy.ActionPoint);
}
