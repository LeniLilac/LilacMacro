using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;
using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class PlacementMapCatalogTests
{
    [Fact]
    public void DefinitionsHaveStableUniqueKeys()
    {
        Assert.Equal(17, PlacementMapCatalog.Definitions.Count);
        Assert.Equal(
            PlacementMapCatalog.Definitions.Count,
            PlacementMapCatalog.Definitions.Select(definition => definition.Id).Distinct().Count());
        Assert.Equal(
            PlacementMapCatalog.Definitions.Sum(definition => definition.DatasetNames.Count),
            PlacementMapCatalog.Definitions.SelectMany(definition => definition.DatasetNames).Distinct().Count());
    }

    [Fact]
    public void BundledCatalogRequiresEveryDeclaredView()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-bundled-maps-{Guid.NewGuid():N}");
        try
        {
            string assets = Path.Combine(root, "Assets", "PlacementMaps");
            Directory.CreateDirectory(assets);
            foreach (PlacementMapDefinition definition in PlacementMapCatalog.Definitions)
            {
                int viewCount = definition.Id == "story-kings-tomb" ? 4
                    : definition.Mode == PlacementMapMode.Expedition ? 1
                    : 2;
                for (int index = 1; index <= viewCount; index++)
                    File.WriteAllBytes(Path.Combine(assets, $"{definition.Id}-{index}.jpg"), [0xff, 0xd8, 0xff, 0xd9]);
            }

            IReadOnlyList<PlacementMapReference> maps = BundledPlacementMapCatalog.Discover(root);

            Assert.Equal(PlacementMapCatalog.Definitions.Count, maps.Count);
            Assert.Equal(32, maps.Sum(map => map.ImagePaths.Count));
            Assert.All(maps, map =>
            {
                Assert.Empty(map.DatasetDirectories);
                Assert.Equal(1366, map.ImageWidth);
                Assert.Equal(700, map.ImageHeight);
            });
            File.Delete(maps[0].ImagePaths[0]);
            Assert.Throws<InvalidDataException>(() => BundledPlacementMapCatalog.Discover(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocalMapOverridesOnlyItsBundledDefinition()
    {
        PlacementMapDefinition story = PlacementMapCatalog.Definitions[0];
        PlacementMapDefinition raid = PlacementMapCatalog.Definitions[6];
        PlacementMapReference bundledStory = Reference(story, "bundled-story.jpg");
        PlacementMapReference bundledRaid = Reference(raid, "bundled-raid.jpg");
        PlacementMapReference localStory = Reference(story, "local-story.png");

        IReadOnlyList<PlacementMapReference> result = BundledPlacementMapCatalog.PreferLocal(
            [bundledStory, bundledRaid],
            [localStory]);

        Assert.Same(localStory, result[0]);
        Assert.Same(bundledRaid, result[1]);
    }

    private static PlacementMapReference Reference(PlacementMapDefinition definition, string path) =>
        new(definition, [], [path], 1366, 700);

    [Fact]
    public async Task DiscoverIncludesFinalizedEastTownStoryAndExpeditionReferences()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-east-town-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            DatasetStore store = new();
            await CreateDatasetAsync(store, root, "East Town Story Map Views", DateTimeOffset.UtcNow, 1);
            await CreateDatasetAsync(store, root, "Expedition East Town Map Preview", DateTimeOffset.UtcNow, 2);

            IReadOnlyList<PlacementMapReference> result = await new PlacementMapCatalog(store).DiscoverAsync(root);

            Assert.Collection(
                result,
                story => Assert.Equal("story-east-town", story.Definition.Id),
                expedition => Assert.Equal("expedition-east-town", expedition.Definition.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
