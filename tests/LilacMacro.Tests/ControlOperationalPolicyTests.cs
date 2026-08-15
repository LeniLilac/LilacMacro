using System.Text;
using LilacMacro.Core.Services;

namespace LilacMacro.Tests;

public sealed class ControlOperationalPolicyTests
{
    private static SignedControlSnapshot Snapshot => ControlSnapshotTests.CreateVerifier()
        .VerifySignature(Encoding.UTF8.GetBytes(ControlSnapshotTests.FixtureJson));

    [Fact]
    public void Missing_snapshot_does_not_expand_or_narrow_local_behavior()
    {
        Assert.True(ControlOperationalPolicy.IsGameAvailable(null));
        Assert.True(ControlOperationalPolicy.IsFeatureEnabled(
            null,
            "mode.expedition",
            ControlSnapshotTests.FixtureNow));
        Assert.Empty(ControlOperationalPolicy.ActiveCodes(null, ControlSnapshotTests.FixtureNow));
        Assert.Null(ControlOperationalPolicy.Schedule(null, ControlScheduleKeys.GoldShopReset));
    }

    [Fact]
    public void Active_disablement_narrows_only_its_exact_feature()
    {
        Assert.False(ControlOperationalPolicy.IsFeatureEnabled(
            Snapshot,
            "task.raid-shop",
            ControlSnapshotTests.FixtureNow));
        Assert.True(ControlOperationalPolicy.IsFeatureEnabled(
            Snapshot,
            "task.gold-shop",
            ControlSnapshotTests.FixtureNow));
        Assert.True(ControlOperationalPolicy.IsFeatureEnabled(
            Snapshot,
            "task.raid-shop",
            DateTimeOffset.Parse("2026-08-14T14:00:00.000Z")));
    }

    [Fact]
    public void Codes_and_schedules_respect_time_and_closed_identifiers()
    {
        Assert.Equal(
            ["WELCOME_2026"],
            ControlOperationalPolicy.ActiveCodes(Snapshot, ControlSnapshotTests.FixtureNow));
        ControlSchedule schedule = Assert.IsType<ControlSchedule>(
            ControlOperationalPolicy.Schedule(Snapshot, ControlScheduleKeys.GoldShopReset));
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-15T00:00:00.000Z"),
            ControlOperationalPolicy.NextScheduledOccurrence(
                schedule,
                DateTimeOffset.Parse("2026-08-14T23:00:00.000Z")));
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-16T00:00:00.000Z"),
            ControlOperationalPolicy.NextScheduledOccurrence(
                schedule,
                DateTimeOffset.Parse("2026-08-15T00:00:00.000Z")));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ControlOperationalPolicy.Schedule(Snapshot, "unknown"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ControlOperationalPolicy.Schedule(Snapshot, "raid-shop-beacon"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlOperationalPolicy.IsFeatureEnabled(
            Snapshot,
            "unknown",
            ControlSnapshotTests.FixtureNow));
    }

    [Fact]
    public void Unavailable_game_uses_operator_message_without_inventing_remote_authority()
    {
        SignedControlSnapshot unavailable = Snapshot with
        {
            Payload = Snapshot.Payload with
            {
                Game = new ControlGameAvailability(
                    Available: false,
                    OperatorAvailable: false,
                    ObservedPublic: null,
                    ObservedAt: null,
                    Message: "Game update in progress"),
            },
        };

        Assert.False(ControlOperationalPolicy.IsGameAvailable(unavailable));
        Assert.Equal(
            "Game update in progress",
            ControlOperationalPolicy.GameUnavailableMessage(unavailable));
    }
}
