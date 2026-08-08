using LilacMacro.App.Views;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementSetupTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void TeamSlotsOneThroughEightAreValid(int teamSlot)
    {
        PlacementRouteSetup route = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        route.TeamSlot = teamSlot;

        PlacementSetupRules.ValidateRoute(route, 1366, 700);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void TeamSlotsOutsideOneThroughEightAreRejected(int teamSlot)
    {
        PlacementRouteSetup route = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        route.TeamSlot = teamSlot;

        Assert.Throws<InvalidDataException>(() => PlacementSetupRules.ValidateRoute(route, 1366, 700));
    }

    [Fact]
    public void StoryRoutesExposeSharedAndPerActSetups()
    {
        PlacementMapDefinition story = new(
            "story-school-grounds",
            PlacementMapMode.Story,
            "School Grounds",
            ["Story School Grounds Map"]);

        IReadOnlyList<PlacementRouteDefinition> routes = PlacementRouteCatalog.For(story);

        Assert.Equal(
            ["SHARED", "ACT 1", "ACT 2", "ACT 3", "ACT 4", "ACT 5", "INFINITE", "MASTERY", "CHALLENGE"],
            routes.Select(route => route.Label));
        Assert.True(routes[0].IsShared);
    }

    [Fact]
    public void ExactRouteUsesSharedUntilAnOverrideExists()
    {
        PlacementSetupDocument document = PlacementSetupRules.CreateDocument("story-school-grounds", 1366, 700);
        PlacementRouteDefinition actTwo = new("act-2", "ACT 2");

        Assert.True(PlacementRouteCatalog.UsesShared(document, actTwo));
        Assert.Same(document.Shared, PlacementRouteCatalog.EffectiveRoute(document, actTwo));

        PlacementRouteSetup custom = PlacementSetupRules.CloneRoute(document.Shared, actTwo.Id);
        custom.TeamSlot = 3;
        document.Overrides.Add(actTwo.Id, custom);

        Assert.False(PlacementRouteCatalog.UsesShared(document, actTwo));
        Assert.Same(custom, PlacementRouteCatalog.EffectiveRoute(document, actTwo));
        Assert.Equal(3, PlacementRouteCatalog.EffectiveRoute(document, actTwo).TeamSlot);
    }

    [Fact]
    public void CloneRouteRemapsPlacementReferences()
    {
        PlacementRouteSetup shared = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        PlacementStep placement = PlacementStep.CreatePlace(
            2,
            200,
            300,
            900,
            PlacementTargetingPriority.First,
            PlacementAutoUpgradePriority.Priority1);
        shared.Steps.Insert(0, placement);
        shared.Steps.Add(new PlacementStep
        {
            Kind = PlacementStepKind.Upgrade,
            TargetPlacementId = placement.Id,
            UnitSlot = 2,
            UpgradeCount = 4,
        });

        PlacementRouteSetup clone = PlacementSetupRules.CloneRoute(shared, "act-1");

        PlacementStep clonedPlacement = clone.Steps[0];
        PlacementStep clonedUpgrade = clone.Steps[2];
        Assert.NotEqual(placement.Id, clonedPlacement.Id);
        Assert.Equal(clonedPlacement.Id, clonedUpgrade.TargetPlacementId);
        PlacementSetupRules.ValidateRoute(clone, 1366, 700);
    }

    [Fact]
    public void UnitActionCannotMoveBeforeItsPlacement()
    {
        PlacementRouteSetup route = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        PlacementStep placement = PlacementStep.CreatePlace(
            1,
            100,
            100,
            900,
            PlacementTargetingPriority.First,
            PlacementAutoUpgradePriority.Priority1);
        route.Steps.Insert(0, placement);
        route.Steps.Add(new PlacementStep
        {
            Kind = PlacementStepKind.Sell,
            TargetPlacementId = placement.Id,
        });
        PlacementStep sell = route.Steps[2];
        route.Steps.RemoveAt(2);
        route.Steps.Insert(0, sell);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            PlacementSetupRules.ValidateRoute(route, 1366, 700));

        Assert.Contains("earlier placement", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreRoundTripsAnAtomicPerMapDocument()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-store-{Guid.NewGuid():N}");
        try
        {
            PlacementSetupStore store = new(root);
            PlacementSetupDocument document = PlacementSetupRules.CreateDocument(
                "story-school-grounds",
                1366,
                700);
            PlacementRouteSetup actFive = PlacementSetupRules.CloneRoute(document.Shared, "act-5");
            actFive.TeamSlot = 5;
            document.Overrides.Add(actFive.RouteId, actFive);

            await store.SaveAsync(document);
            PlacementSetupDocument loaded = await store.LoadOrCreateAsync(
                "story-school-grounds",
                1366,
                700);

            Assert.Equal(5, loaded.Overrides["act-5"].TeamSlot);
            Assert.Single(Directory.EnumerateFiles(root, "*.json"));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EditorSessionMovesStepDirectlyToDroppedPosition()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-reorder-{Guid.NewGuid():N}");
        try
        {
            PlacementSetupStore store = new(root);
            PlacementEditorSession session = new(store);
            PlacementMapDefinition map = new(
                "expedition-school-grounds",
                PlacementMapMode.Expedition,
                "School Grounds",
                ["Expedition School Grounds Map"]);
            await session.OpenAsync(map, 1366, 700);
            await session.AddDelayAsync();

            await session.MoveStepToAsync(1, 0);
            await session.FlushAsync();

            Assert.Equal(PlacementStepKind.Delay, session.CurrentRoute.Steps[0].Kind);
            Assert.Equal(PlacementStepKind.StartGame, session.CurrentRoute.Steps[1].Kind);
            PlacementSetupDocument saved = await store.LoadOrCreateAsync(map.Id, 1366, 700);
            Assert.Equal(PlacementStepKind.Delay, saved.Shared.Steps[0].Kind);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResettingExactRouteRemovesOverrideAndRestoresSharedRoute()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-reset-{Guid.NewGuid():N}");
        try
        {
            PlacementEditorSession session = new(new PlacementSetupStore(root));
            PlacementMapDefinition map = new(
                "story-school-grounds",
                PlacementMapMode.Story,
                "School Grounds",
                ["Story School Grounds Map"]);
            await session.OpenAsync(map, 1366, 700);
            session.SelectRoute("act-1");
            await session.AddDelayAsync();

            Assert.False(session.UsesShared);
            Assert.True(session.CanReset);

            await session.ResetAsync();

            Assert.True(session.UsesShared);
            Assert.Same(session.Document!.Shared, session.CurrentRoute);
            Assert.False(session.CanReset);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResettingSharedRouteKeepsOnlyRequiredStartBoundaryAndDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-shared-reset-{Guid.NewGuid():N}");
        try
        {
            PlacementEditorSession session = new(new PlacementSetupStore(root));
            PlacementMapDefinition map = new(
                "expedition-school-grounds",
                PlacementMapMode.Expedition,
                "School Grounds",
                ["Expedition School Grounds Map"]);
            await session.OpenAsync(map, 1366, 700);
            await session.SetRouteDefaultsAsync(
                8,
                6,
                900,
                PlacementTargetingPriority.Last,
                PlacementAutoUpgradePriority.Priority2);
            await session.AddPlacementAsync(200, 200);
            await session.AddDelayAsync();

            await session.ResetAsync();

            PlacementStep start = Assert.Single(session.CurrentRoute.Steps);
            Assert.Equal(PlacementStepKind.StartGame, start.Kind);
            Assert.Equal(8, session.CurrentRoute.TeamSlot);
            Assert.Equal(6, session.CurrentRoute.SelectedUnitSlot);
            Assert.False(session.CanReset);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeletingPlacementAlsoDeletesEveryDependentUnitAction()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-delete-{Guid.NewGuid():N}");
        try
        {
            PlacementEditorSession session = new(new PlacementSetupStore(root));
            PlacementMapDefinition map = new(
                "expedition-school-grounds",
                PlacementMapMode.Expedition,
                "School Grounds",
                ["Expedition School Grounds Map"]);
            await session.OpenAsync(map, 1366, 700);
            await session.AddPlacementAsync(200, 200);
            Guid placementId = session.CurrentRoute.Steps.Single(step => step.Kind == PlacementStepKind.Place).Id;
            await session.AddUnitActionAsync(PlacementStepKind.Reconfigure, placementId);
            await session.AddUnitActionAsync(PlacementStepKind.Upgrade, placementId);
            await session.AddUnitActionAsync(PlacementStepKind.Sell, placementId);

            int placementIndex = session.CurrentRoute.Steps.FindIndex(step => step.Id == placementId);
            await session.DeleteStepAsync(placementIndex);

            Assert.Single(session.CurrentRoute.Steps);
            Assert.Equal(PlacementStepKind.StartGame, session.CurrentRoute.Steps[0].Kind);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MovingPlacementPreservesIdentityReferencesAndSavedCoordinates()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-placement-move-{Guid.NewGuid():N}");
        try
        {
            PlacementSetupStore store = new(root);
            PlacementEditorSession session = new(store);
            PlacementMapDefinition map = new(
                "expedition-school-grounds",
                PlacementMapMode.Expedition,
                "School Grounds",
                ["Expedition School Grounds Map"]);
            await session.OpenAsync(map, 1366, 700);
            await session.AddPlacementAsync(200, 200);
            PlacementStep placement = session.CurrentRoute.Steps
                .Single(step => step.Kind == PlacementStepKind.Place);
            await session.AddUnitActionAsync(PlacementStepKind.Upgrade, placement.Id);

            await session.MovePlacementAsync(placement.Id, 520, 410);
            await session.FlushAsync();

            PlacementStep moved = session.CurrentRoute.Steps.Single(step => step.Id == placement.Id);
            PlacementStep action = session.CurrentRoute.Steps
                .Single(step => step.Kind == PlacementStepKind.Upgrade);
            Assert.Equal((520, 410), (moved.X, moved.Y));
            Assert.Equal(placement.Id, action.TargetPlacementId);

            PlacementSetupDocument saved = await store.LoadOrCreateAsync(map.Id, 1366, 700);
            PlacementStep savedPlacement = saved.Shared.Steps.Single(step => step.Id == placement.Id);
            Assert.Equal((520, 410), (savedPlacement.X, savedPlacement.Y));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
