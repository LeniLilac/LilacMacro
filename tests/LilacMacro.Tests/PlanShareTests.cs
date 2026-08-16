using System.IO.Compression;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Views;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlanShareTests
{
    [Fact]
    public void Bundle_round_trips_selected_plan_and_valid_placements()
    {
        PlanPrototype plan = PlanPrototypeFactory.CreatePlans()[0];
        PlacementSetupDocument placement = PlacementSetupRules.CreateDocument(
            PlacementMapCatalog.Definitions[0].Id,
            1366,
            700);
        PlanShareBundle source = new()
        {
            Plan = PlanPersistence.CreateSnapshot([plan]).Single(),
            Placements = [placement],
        };

        PlanShareBundle restored = PlanShareBundleCodec.Decode(PlanShareBundleCodec.Encode(source));

        Assert.Equal(plan.Name, restored.Plan?.Name);
        Assert.Single(restored.Placements);
        Assert.Equal(placement.MapId, restored.Placements[0].MapId);
    }

    [Fact]
    public void Bundle_rejects_duplicate_placement_maps()
    {
        PlacementSetupDocument placement = PlacementSetupRules.CreateDocument(
            PlacementMapCatalog.Definitions[0].Id,
            1366,
            700);
        PlanShareBundle source = new() { Placements = [placement, placement] };

        Assert.Throws<InvalidDataException>(() => PlanShareBundleCodec.Encode(source));
    }

    [Fact]
    public void Bundle_rejects_a_small_compressed_payload_that_expands_past_the_limit()
    {
        using MemoryStream compressed = new();
        using (BrotliStream stream = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            stream.Write(new byte[2 * 1024 * 1024 + 1]);
        string payload = Convert.ToBase64String(compressed.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Throws<InvalidDataException>(() => PlanShareBundleCodec.Decode(payload));
    }

    [Theory]
    [InlineData("23456-789ab-cdefg-hjkmn", "23456789ABCDEFGHJKMN")]
    [InlineData("  JKMP5 6789A BCDEF GH234  ", "JKMP56789ABCDEFGH234")]
    public void Share_codes_are_normalized(string input, string expected) =>
        Assert.Equal(expected, PlanShareClient.NormalizeCode(input));

    [Theory]
    [InlineData("11111111111111111111")]
    [InlineData("OOOOOOOOOOOOOOOOOOOO")]
    [InlineData("SHORT")]
    public void Ambiguous_or_malformed_share_codes_are_rejected(string input) =>
        Assert.Throws<InvalidDataException>(() => PlanShareClient.NormalizeCode(input));

    [Fact]
    public void Bundle_rejects_missing_nested_placement_state()
    {
        PlacementSetupDocument placement = PlacementSetupRules.CreateDocument(
            PlacementMapCatalog.Definitions[0].Id,
            1366,
            700);
        placement.Overrides = null!;

        Assert.Throws<InvalidDataException>(() =>
            PlanShareBundleCodec.Encode(new PlanShareBundle { Placements = [placement] }));
    }

    [Fact]
    public void Bundle_rejects_a_missing_step_before_inspecting_step_members()
    {
        PlacementSetupDocument placement = PlacementSetupRules.CreateDocument(
            PlacementMapCatalog.Definitions[0].Id,
            1366,
            700);
        placement.Shared.Steps.Insert(0, null!);

        Assert.Throws<InvalidDataException>(() =>
            PlanShareBundleCodec.Encode(new PlanShareBundle { Placements = [placement] }));
    }

    [Fact]
    public void Bundle_rejects_unknown_json_members()
    {
        const string json =
            "{\"schema_version\":1,\"plan\":null,\"placements\":[],\"unexpected\":true}";
        using MemoryStream compressed = new();
        using (BrotliStream stream = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
        string payload = Convert.ToBase64String(compressed.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Throws<InvalidDataException>(() => PlanShareBundleCodec.Decode(payload));
    }

    [Fact]
    public void Configuration_mutation_is_rejected_while_a_run_lease_is_held()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-share-gate-{Guid.NewGuid():N}");
        ConfigurationMutationGate runnerGate = new(root);
        ConfigurationMutationGate ownerGate = new(root);
        using IDisposable run = runnerGate.AcquireRunLease();

        Assert.StartsWith(Path.GetFullPath(root), ownerGate.LockPath, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => ownerGate.AcquireMutationLease());
    }
}
