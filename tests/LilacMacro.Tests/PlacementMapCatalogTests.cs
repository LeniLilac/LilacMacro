using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementMapCatalogTests
{
    [Fact]
    public void DefinitionsHaveStableUniqueKeys()
    {
        Assert.Equal(11, PlacementMapCatalog.Definitions.Count);
        Assert.Equal(
            PlacementMapCatalog.Definitions.Count,
            PlacementMapCatalog.Definitions.Select(definition => definition.Id).Distinct().Count());
        Assert.Equal(
            PlacementMapCatalog.Definitions.Sum(definition => definition.DatasetNames.Count),
            PlacementMapCatalog.Definitions.SelectMany(definition => definition.DatasetNames).Distinct().Count());
    }

    [Fact]
    public async Task DiscoverUsesLatestFinalizedDatasetAndDefinitionOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            DatasetStore store = new();
            await CreateDatasetAsync(store, root, "Story School Grounds Map", new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero), 1);
            DatasetLocation latestStory = await CreateDatasetAsync(
                store,
                root,
                "Story School Grounds Map",
                new DateTimeOffset(2026, 8, 2, 1, 0, 0, TimeSpan.Zero),
                2);
            DatasetLocation raid = await CreateDatasetAsync(
                store,
                root,
                "Raid Spirit City Act 2 Map",
                new DateTimeOffset(2026, 8, 2, 2, 0, 0, TimeSpan.Zero),
                3);
            await CreateDatasetAsync(store, root, "Unrelated Dataset", DateTimeOffset.UtcNow, 4);

            PlacementMapCatalog catalog = new(store);
            IReadOnlyList<PlacementMapReference> result = await catalog.DiscoverAsync(root);

            Assert.Collection(
                result,
                story =>
                {
                    Assert.Equal("story-school-grounds", story.Definition.Id);
                    Assert.Equal([latestStory.DirectoryPath], story.DatasetDirectories);
                    Assert.Single(story.ImagePaths);
                    Assert.Equal(1366, story.ImageWidth);
                    Assert.Equal(700, story.ImageHeight);
                },
                foundRaid =>
                {
                    Assert.Equal("raid-spirit-city-act-2", foundRaid.Definition.Id);
                    Assert.Equal([raid.DirectoryPath], foundRaid.DatasetDirectories);
                });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverCombinesReferenceAnglesForOneMap()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-angles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            DatasetStore store = new();
            DatasetLocation angleOne = await CreateDatasetAsync(
                store,
                root,
                "Story King's Tomb Map Angle 1",
                new DateTimeOffset(2026, 8, 2, 1, 0, 0, TimeSpan.Zero),
                1);
            DatasetLocation angleTwo = await CreateDatasetAsync(
                store,
                root,
                "Story King's Tomb Map Angle 2",
                new DateTimeOffset(2026, 8, 2, 2, 0, 0, TimeSpan.Zero),
                2);

            PlacementMapReference map = Assert.Single(await new PlacementMapCatalog(store).DiscoverAsync(root));

            Assert.Equal("story-kings-tomb", map.Definition.Id);
            Assert.Equal([angleOne.DirectoryPath, angleTwo.DirectoryPath], map.DatasetDirectories);
            Assert.Equal(2, map.ImagePaths.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<DatasetLocation> CreateDatasetAsync(
        DatasetStore store,
        string root,
        string name,
        DateTimeOffset createdAt,
        byte marker)
    {
        DatasetLocation draft = await store.CreateManualDraftAsync(
            root,
            new PixelSize(1366, 700),
            "Roblox",
            100,
            createdAt);
        await store.AddFrameAsync(draft, new byte[] { marker, 0x50, 0x4e, 0x47 }, 1366, 700, createdAt);
        return await store.FinalizeAsync(draft, name, string.Empty);
    }
}
