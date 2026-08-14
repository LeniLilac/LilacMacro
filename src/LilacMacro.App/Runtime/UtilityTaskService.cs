using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class UtilityTaskService
{
    private readonly ResourceRefuelService _refuel;
    private readonly ShopPurchaseService _shops;
    private readonly CalendarClaimService _calendar;

    public UtilityTaskService(WorkspaceController workspace, OcrRunner ocr)
    {
        UtilityRespawnService respawn = new(workspace, ocr);
        _refuel = new ResourceRefuelService(workspace, ocr, respawn);
        _shops = new ShopPurchaseService(workspace, ocr, respawn);
        _calendar = new CalendarClaimService(workspace, ocr, respawn);
    }

    public Task RunAsync(
        string route,
        IReadOnlyList<string> shopItemIds,
        int? areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        UtilityTaskPolicy.Validate(route, shopItemIds);
        if (ShopPurchasePolicy.IsShopRoute(route))
        {
            return _shops.RunAsync(
                route, shopItemIds, areasMenuVirtualKey, reservedVirtualKey,
                device, status, cancellationToken);
        }
        if (string.Equals(route, UtilityTaskPolicy.CalendarClaimRoute, StringComparison.Ordinal))
        {
            return _calendar.RunAsync(
                areasMenuVirtualKey, reservedVirtualKey, device, status, cancellationToken);
        }
        return _refuel.RunRouteAsync(
            route, areasMenuVirtualKey, reservedVirtualKey,
            device, status, cancellationToken);
    }
}
