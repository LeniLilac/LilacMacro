using LilacMacro.App.Debugging;
using LilacMacro.App.Runtime;
using LilacMacro.App.Views;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class MatchContinuationPolicyTests
{
    [Theory]
    [InlineData(PlanTaskMode.Story, true)]
    [InlineData(PlanTaskMode.Raid, true)]
    [InlineData(PlanTaskMode.Expedition, true)]
    [InlineData(PlanTaskMode.Event, true)]
    [InlineData(PlanTaskMode.Challenge, false)]
    [InlineData(PlanTaskMode.Utilities, false)]
    public void MacroModeRepeatPolicyMatchesRuntimeContract(PlanTaskMode mode, bool expected) =>
        Assert.Equal(expected, MacroTaskRepeatPolicy.Supports(mode));

    [Theory]
    [InlineData((int)WireGameMode.Story, true)]
    [InlineData((int)WireGameMode.Raid, true)]
    [InlineData((int)WireGameMode.Expedition, true)]
    [InlineData((int)WireGameMode.Event, true)]
    [InlineData((int)WireGameMode.Challenge, false)]
    public void WireModeRepeatPolicyMatchesRuntimeContract(int mode, bool expected) =>
        Assert.Equal(expected, WireGameModeRepeatPolicy.Supports((WireGameMode)mode));

    [Fact]
    public void RepeatsVerifiedSupportedSameTask() =>
        Assert.True(MatchContinuationPolicy.ShouldRepeat(
            hasVerifiedTerminalOutcome: true,
            modeSupportsRepeat: true,
            sameTaskSelected: true));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ResetsWhenAnyRequiredConditionIsAbsent(
        bool hasVerifiedTerminalOutcome,
        bool modeSupportsRepeat,
        bool sameTaskSelected) =>
        Assert.False(MatchContinuationPolicy.ShouldRepeat(
            hasVerifiedTerminalOutcome,
            modeSupportsRepeat,
            sameTaskSelected));

    [Fact]
    public void TeamStateIsScopedAndReusableOnlyForExactTeam()
    {
        MacroRunTeamState state = new();

        Assert.False(state.CanReuse(3));
        state.MarkLoaded(3);
        Assert.True(state.CanReuse(3));
        Assert.False(state.CanReuse(4));
    }

    [Fact]
    public void RetainedPhysicalExpeditionPlacementLeavesFutureReplayCandidates()
    {
        PlacementExecutionState retained = Placement(1, 100, 200);
        PlacementExecutionState phantom = Placement(2, 300, 400);
        ExpeditionPlacementSession session = new([retained, phantom], panelLayout: null);

        session.MarkRetainedPhysical(retained.Placement.Id);

        Assert.True(session.IsRetainedPhysical(retained.Placement.Id));
        Assert.Equal([phantom], session.ReplayCandidates);
    }

    [Fact]
    public void ExpeditionRetentionRejectsUnknownPlacement()
    {
        ExpeditionPlacementSession session = new([Placement(1, 100, 200)], panelLayout: null);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.MarkRetainedPhysical(Guid.NewGuid()));
    }

    private static PlacementExecutionState Placement(int slot, int x, int y) => new(
        PlacementStep.CreatePlace(
            slot,
            x,
            y,
            PlacementTargetingPriority.First,
            PlacementAutoUpgradePriority.Off),
        new PixelPoint(x, y));
}
