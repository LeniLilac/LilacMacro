using System.Text;
using LilacMacro.App.Runtime;
using LilacMacro.App.Views;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Services;

namespace LilacMacro.Tests;

public sealed class MacroControlPolicyTests
{
    private static SignedControlSnapshot Snapshot => ControlSnapshotTests.CreateVerifier()
        .VerifySignature(Encoding.UTF8.GetBytes(ControlSnapshotTests.FixtureJson));

    [Fact]
    public void Exact_mode_and_utility_disablements_do_not_affect_other_tasks()
    {
        PlanTaskPrototype raidShop = Task(PlanTaskMode.Utilities, ShopPurchasePolicy.RaidRoute);
        PlanTaskPrototype goldShop = Task(PlanTaskMode.Utilities, ShopPurchasePolicy.GoldRoute);

        Assert.False(MacroControlPolicy.IsTaskEnabled(
            Snapshot,
            raidShop,
            ControlSnapshotTests.FixtureNow));
        Assert.True(MacroControlPolicy.IsTaskEnabled(
            Snapshot,
            goldShop,
            ControlSnapshotTests.FixtureNow));
        Assert.True(MacroControlPolicy.IsTaskEnabled(
            null,
            raidShop,
            ControlSnapshotTests.FixtureNow));
    }

    [Fact]
    public void Combined_refuel_requires_both_independent_features()
    {
        SignedControlSnapshot snapshot = WithDisablement("task.gold-mine-refuel");
        PlanTaskPrototype combined = Task(
            PlanTaskMode.Utilities,
            ResourceRefuelPolicy.CombinedRoute);

        Assert.False(MacroControlPolicy.IsTaskEnabled(
            snapshot,
            combined,
            ControlSnapshotTests.FixtureNow));
        Assert.True(MacroControlPolicy.IsTaskEnabled(
            snapshot,
            Task(PlanTaskMode.Utilities, ResourceRefuelPolicy.ResourceDrillRoute),
            ControlSnapshotTests.FixtureNow));
    }

    [Fact]
    public void Expedition_optimizer_requires_mode_and_optimizer_features()
    {
        PlanTaskPrototype regular = Task(PlanTaskMode.Expedition, "School Grounds");
        PlanTaskPrototype optimized = Task(PlanTaskMode.Expedition, "School Grounds");
        optimized.RewardTarget = "Fuel Cell";
        SignedControlSnapshot snapshot = WithDisablement("feature.route-optimizer");

        Assert.True(MacroControlPolicy.IsTaskEnabled(
            snapshot,
            regular,
            ControlSnapshotTests.FixtureNow));
        Assert.False(MacroControlPolicy.IsTaskEnabled(
            snapshot,
            optimized,
            ControlSnapshotTests.FixtureNow));
    }

    [Fact]
    public void Fresh_remote_schedule_overrides_local_shop_beacon_in_either_direction()
    {
        PlanTaskPrototype task = Task(PlanTaskMode.Utilities, ShopPurchasePolicy.GoldRoute);
        DateTimeOffset beforeReset = DateTimeOffset.Parse("2026-08-14T12:05:00.000Z");
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-15T00:00:00.000Z"),
            MacroControlPolicy.NextUtilityDue(Snapshot, task, beforeReset));

        SignedControlSnapshot delayed = Snapshot with
        {
            Payload = Snapshot.Payload with
            {
                Schedules =
                [
                    new ControlSchedule(
                        ControlScheduleKeys.GoldShopReset,
                        DateTimeOffset.Parse("2026-08-16T00:00:00.000Z"),
                        86_400),
                ],
            },
        };
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-16T00:00:00.000Z"),
            MacroControlPolicy.NextUtilityDue(delayed, task, beforeReset));

        SignedControlSnapshot accelerated = Snapshot with
        {
            Payload = Snapshot.Payload with
            {
                Schedules =
                [
                    new ControlSchedule(
                        ControlScheduleKeys.GoldShopReset,
                        DateTimeOffset.Parse("2026-08-14T18:00:00.000Z"),
                        86_400),
                ],
            },
        };
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-14T18:00:00.000Z"),
            MacroControlPolicy.NextUtilityDue(accelerated, task, beforeReset));
    }

    private static PlanTaskPrototype Task(PlanTaskMode mode, string route) => new()
    {
        Mode = mode,
        Route = route,
        Target = 400,
    };

    private static SignedControlSnapshot WithDisablement(string feature) => Snapshot with
    {
        Payload = Snapshot.Payload with
        {
            Disablements =
            [
                new ControlDisablement(feature, "Temporarily paused", null),
            ],
        },
    };
}
