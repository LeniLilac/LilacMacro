using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class ExpeditionRewardRegressionTests
{
    [Theory]
    [InlineData("3bx", ExpeditionRewardResource.FuelCell, 31)]
    [InlineData("31bx", ExpeditionRewardResource.FuelCell, 31)]
    [InlineData("4bx", ExpeditionRewardResource.FuelCell, 41)]
    [InlineData("4kx", ExpeditionRewardResource.EquipmentScrap, 41)]
    [InlineData("41bx", ExpeditionRewardResource.ExpeditionCoin, 41)]
    [InlineData("5bx", ExpeditionRewardResource.EquipmentScrap, 51)]
    [InlineData("5kx", ExpeditionRewardResource.ExpeditionCoin, 51)]
    [InlineData("51bx", ExpeditionRewardResource.FuelCell, 51)]
    [InlineData("1bx", ExpeditionRewardResource.EquipmentLock, 11)]
    public void PostUpdateTrailingOneGlyphsAreResourceScoped(
        string text,
        ExpeditionRewardResource resource,
        int expected) =>
        Assert.Equal(expected, ExpeditionRewardPolicy.ParseQuantity(text, resource));

    [Theory]
    [InlineData("2bx", ExpeditionRewardResource.ExpeditionCoin)]
    [InlineData("3bx", ExpeditionRewardResource.EquipmentLock)]
    [InlineData("5kx", ExpeditionRewardResource.EquipmentReroll)]
    [InlineData("1bx", ExpeditionRewardResource.FuelCell)]
    public void UnsupportedCrossResourceGlyphsRemainUnreadable(
        string text,
        ExpeditionRewardResource resource) =>
        Assert.Null(ExpeditionRewardPolicy.ParseQuantity(text, resource));

    [Fact]
    public void CompletePoolComparisonRequiresAllFiveMatchingQuantities()
    {
        ExpeditionRewardPool first = CompletePool(fuelCell: 31);
        ExpeditionRewardPool same = CompletePool(fuelCell: 31);
        ExpeditionRewardPool changed = CompletePool(fuelCell: 41);
        ExpeditionRewardPool partial = new(new Dictionary<ExpeditionRewardResource, int>
        {
            [ExpeditionRewardResource.FuelCell] = 31,
        });

        Assert.True(first.IsComplete);
        Assert.False(partial.IsComplete);
        Assert.True(ExpeditionRewardPolicy.SameCompletePool(first, same));
        Assert.False(ExpeditionRewardPolicy.SameCompletePool(first, changed));
        Assert.False(ExpeditionRewardPolicy.SameCompletePool(first, partial));
    }

    [Fact]
    public async Task RewardProfileRejectsPartialPoolInsteadOfRecordingFalseZeroes()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-reward-regression-{Guid.NewGuid():N}");
        try
        {
            ExpeditionRewardProfileStore store = new(root);
            ExpeditionRewardPool partial = new(new Dictionary<ExpeditionRewardResource, int>
            {
                [ExpeditionRewardResource.FuelCell] = 31,
            });

            await Assert.ThrowsAsync<InvalidDataException>(() => store.RecordPoolAsync(3, partial));
            Assert.Equal((547, 0, 10d), await store.StatusAsync(3, "gpu:0"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(1, 1019)]
    [InlineData(2, 1000)]
    [InlineData(3, 547)]
    public void BundledPostUpdatePriorsCoverEveryResource(int difficulty, int expectedPools)
    {
        Assert.Equal(expectedPools, ExpeditionRewardPriorCatalog.PoolCount(difficulty));
        foreach (ExpeditionRewardResource resource in Enum.GetValues<ExpeditionRewardResource>()
                     .Where(resource => resource != ExpeditionRewardResource.None))
        {
            Assert.Equal(expectedPools, ExpeditionRewardPriorCatalog.Histogram(difficulty, resource).Values.Sum());
        }
    }

    [Theory]
    [InlineData(1, ExpeditionRewardResource.FuelCell, 28)]
    [InlineData(1, ExpeditionRewardResource.EquipmentScrap, 27)]
    [InlineData(1, ExpeditionRewardResource.EquipmentReroll, 3)]
    [InlineData(1, ExpeditionRewardResource.EquipmentLock, 4)]
    [InlineData(1, ExpeditionRewardResource.ExpeditionCoin, 29)]
    [InlineData(2, ExpeditionRewardResource.FuelCell, 33)]
    [InlineData(2, ExpeditionRewardResource.EquipmentScrap, 32)]
    [InlineData(2, ExpeditionRewardResource.EquipmentReroll, 3)]
    [InlineData(2, ExpeditionRewardResource.EquipmentLock, 6)]
    [InlineData(2, ExpeditionRewardResource.ExpeditionCoin, 34)]
    [InlineData(3, ExpeditionRewardResource.FuelCell, 39)]
    [InlineData(3, ExpeditionRewardResource.EquipmentScrap, 40)]
    [InlineData(3, ExpeditionRewardResource.EquipmentReroll, 6)]
    [InlineData(3, ExpeditionRewardResource.EquipmentLock, 6)]
    [InlineData(3, ExpeditionRewardResource.ExpeditionCoin, 42)]
    public async Task BundledPriorsProduceExpectedThresholds(
        int difficulty,
        ExpeditionRewardResource resource,
        int expectedThreshold)
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-reward-prior-{Guid.NewGuid():N}");
        try
        {
            ExpeditionRewardProfileStore store = new(root);
            await store.RecordRerollAsync("gpu:0", TimeSpan.FromSeconds(7.848026347));

            ExpeditionRewardOptimization? optimization = await store.OptimizeAsync(
                difficulty, resource, "gpu:0");

            Assert.NotNull(optimization);
            Assert.Equal(expectedThreshold, optimization.Threshold);
            Assert.Equal(ExpeditionRewardPriorCatalog.PoolCount(difficulty), optimization.ObservationCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VersionTwoProfileMigrationDropsStalePoolsAndPreservesTiming()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-reward-v2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "expedition-reward-profiles.json"),
                """
                {
                  "version": 2,
                  "difficulties": {
                    "3": {
                      "poolCount": 900,
                      "histograms": { "FuelCell": { "9345": 900 } }
                    }
                  },
                  "rerollSeconds": { "gpu:0": [7.0, 9.0] }
                }
                """);
            ExpeditionRewardProfileStore store = new(root);

            Assert.Equal((547, 2, 8d), await store.StatusAsync(3, "gpu:0"));
            ExpeditionRewardOptimization? optimization = await store.OptimizeAsync(
                3, ExpeditionRewardResource.FuelCell, "gpu:0");
            Assert.NotNull(optimization);
            Assert.Equal(547, optimization.ObservationCount);
            Assert.Equal(39, optimization.Threshold);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ExpeditionRewardPool CompletePool(int fuelCell) => new(
        Enum.GetValues<ExpeditionRewardResource>()
            .Where(resource => resource != ExpeditionRewardResource.None)
            .ToDictionary(resource => resource,
                resource => resource == ExpeditionRewardResource.FuelCell ? fuelCell : 0));
}
