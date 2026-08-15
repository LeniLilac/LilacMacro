using LilacMacro.App.Views;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementCursorModeTests
{
    [Fact]
    public void CompactMarkerKeepsItsPointAtTheSavedCoordinate()
    {
        PlacementMarkerPresentation marker = PlacementMarkerPresentation.Create(200, 300);

        Assert.Equal(176, marker.CanvasLeft);
        Assert.Equal(264, marker.CanvasTop);
        Assert.Equal(66, marker.CanvasWidth);
        Assert.Equal(46, marker.CanvasHeight);
    }

    [Theory]
    [InlineData(200, 300, 200, 300, 1, true)]
    [InlineData(200, 300, 270, 300, 1, true)]
    [InlineData(200, 300, 273, 300, 1, false)]
    [InlineData(200, 300, 330, 300, 0.5, true)]
    [InlineData(200, 300, 345, 300, 0.5, false)]
    public void NearbyMarkerRadiusRemainsConstantInViewportPixels(
        double anchorX,
        double anchorY,
        double pointerX,
        double pointerY,
        double zoom,
        bool expected)
    {
        Assert.Equal(expected, PlacementMarkerPresentation.IsNearPointer(
            anchorX,
            anchorY,
            pointerX,
            pointerY,
            zoom));
    }

    [Theory]
    [InlineData(PlacementCursorMode.Place, false)]
    [InlineData(PlacementCursorMode.Select, true)]
    public void MarkerRowsCarryTheExplicitCursorMode(
        PlacementCursorMode mode,
        bool expectedSelectionMode)
    {
        PlacementRouteSetup route = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        route.Steps.Insert(0, PlacementStep.CreatePlace(
            2,
            200,
            300,
            PlacementTargetingPriority.First,
            PlacementAutoUpgradePriority.Priority1));

        PlacementStepRowViewModel marker = PlacementStepRowFactory.Create(
                route,
                cursorMode: mode)
            .Single(row => row.IsPlacement);

        Assert.Equal(mode, marker.CursorMode);
        Assert.Equal(expectedSelectionMode, marker.IsSelectionMode);
    }

    [Theory]
    [InlineData(PlacementCursorMode.Place, true, 0.18)]
    [InlineData(PlacementCursorMode.Place, false, 1)]
    [InlineData(PlacementCursorMode.Select, true, 1)]
    public void OnlyPlaceModeDimsNearbyMarkerPins(
        PlacementCursorMode mode,
        bool isNear,
        double expectedOpacity)
    {
        PlacementRouteSetup route = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        route.Steps.Insert(0, PlacementStep.CreatePlace(
            2,
            200,
            300,
            PlacementTargetingPriority.First,
            PlacementAutoUpgradePriority.Priority1));

        PlacementStepRowViewModel marker = PlacementStepRowFactory.Create(
                route,
                cursorMode: mode)
            .Single(row => row.IsPlacement);

        marker.SetNearPointer(isNear);

        Assert.Equal(expectedOpacity, marker.PinOpacity);
    }

    [Fact]
    public void SelectionDragDimsNearbyMarkerPins()
    {
        PlacementRouteSetup route = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        route.Steps.Insert(0, PlacementStep.CreatePlace(
            2, 200, 300, PlacementTargetingPriority.First, PlacementAutoUpgradePriority.Priority1));
        PlacementStepRowViewModel marker = PlacementStepRowFactory.Create(
                route, cursorMode: PlacementCursorMode.Select)
            .Single(row => row.IsPlacement);

        marker.SetNearPointer(true, fadeInSelectionMode: true);

        Assert.Equal(0.18, marker.PinOpacity);
    }

    [Fact]
    public void MarkerScaleCounteractsMapZoom()
    {
        PlacementRouteSetup route = PlacementSetupRules.CreateRoute(PlacementRouteCatalog.SharedRouteId);
        route.Steps.Insert(0, PlacementStep.CreatePlace(
            2, 200, 300, PlacementTargetingPriority.First, PlacementAutoUpgradePriority.Priority1));
        PlacementStepRowViewModel marker = PlacementStepRowFactory.Create(route).Single(row => row.IsPlacement);

        marker.SetZoom(2.5);

        Assert.Equal(0.4, marker.MarkerScale, precision: 6);
    }
}
