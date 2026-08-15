using LilacMacro.App.Views;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementSetupCopyTests
{
    [Fact]
    public void CopyIncludesDefaultsAndRemapsPlacementReferences()
    {
        PlacementRouteSetup source = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        PlacementStep place = PlacementStep.CreatePlace(
            4, 100, 200, PlacementTargetingPriority.Last, PlacementAutoUpgradePriority.Priority2);
        source.TeamSlot = 6;
        source.SelectedUnitSlot = 4;
        source.BetweenUpgradeAttemptsMilliseconds = 725;
        source.Steps.Insert(0, place);
        source.Steps.Add(new PlacementStep
        {
            Kind = PlacementStepKind.Upgrade,
            TargetPlacementId = place.Id,
            UnitSlot = 4,
            UpgradeCount = 2,
        });

        PlacementRouteSetup copy = PlacementSetupRules.CopyRouteToSurface(
            source, "act-2", 1366, 700, 683, 350);

        PlacementStep copiedPlace = copy.Steps.Single(step => step.Kind == PlacementStepKind.Place);
        PlacementStep copiedUpgrade = copy.Steps.Single(step => step.Kind == PlacementStepKind.Upgrade);
        Assert.Equal("act-2", copy.RouteId);
        Assert.Equal(6, copy.TeamSlot);
        Assert.Equal(4, copy.SelectedUnitSlot);
        Assert.Equal(725, copy.BetweenUpgradeAttemptsMilliseconds);
        Assert.Equal(50, copiedPlace.X);
        Assert.Equal(100, copiedPlace.Y);
        Assert.NotEqual(place.Id, copiedPlace.Id);
        Assert.Equal(copiedPlace.Id, copiedUpgrade.TargetPlacementId);
    }

    [Fact]
    public void CopyRejectsMalformedSourceCoordinatesInsteadOfClampingThem()
    {
        PlacementRouteSetup source = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        source.Steps.Insert(0, PlacementStep.CreatePlace(
            1, 1366, 699, PlacementTargetingPriority.First, PlacementAutoUpgradePriority.Priority1));

        Assert.Throws<InvalidDataException>(() => PlacementSetupRules.CopyRouteToSurface(
            source, "act-2", 1366, 700, 683, 350));
    }

    [Fact]
    public async Task SessionCopyPersistsTheSelectedTargetRoute()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-copy-{Guid.NewGuid():N}");
        try
        {
            PlacementSetupStore store = new(root);
            PlacementMapDefinition sourceMap = new(
                "story-source",
                PlacementMapMode.Story,
                "Source",
                []);
            PlacementSetupDocument source = PlacementSetupRules.CreateDocument(sourceMap.Id, 1366, 700);
            source.Shared.TeamSlot = 7;
            source.Shared.Steps.Insert(0, PlacementStep.CreatePlace(
                3, 100, 200, PlacementTargetingPriority.First, PlacementAutoUpgradePriority.Priority1));
            await store.SaveAsync(source);

            PlacementMapDefinition targetMap = new(
                "story-target",
                PlacementMapMode.Story,
                "Target",
                []);
            PlacementEditorSession session = new(store);
            await session.OpenAsync(targetMap, 683, 350);
            session.SelectRoute("act-2");

            await session.CopyFromAsync(
                sourceMap,
                new PlacementRouteDefinition(PlacementRouteCatalog.SharedRouteId, "SHARED", IsShared: true),
                683,
                350);
            await session.FlushAsync();

            PlacementSetupDocument saved = await store.LoadAsync(targetMap.Id);
            PlacementRouteSetup copied = saved.Overrides["act-2"];
            PlacementStep placement = copied.Steps.Single(step => step.Kind == PlacementStepKind.Place);
            Assert.Equal(7, copied.TeamSlot);
            Assert.Equal(50, placement.X);
            Assert.Equal(100, placement.Y);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
