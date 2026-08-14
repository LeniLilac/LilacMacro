using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class ShopPurchaseService(
    WorkspaceController workspace,
    OcrRunner ocr,
    UtilityRespawnService respawn)
{
    private static readonly PixelSize ClientSize = DebugWorkflowCatalog.ClientSize;
    private static readonly TimeSpan ObservationDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan TeleportDelay = TimeSpan.FromMilliseconds(5500);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly ObservedStateTransitionRunner _transitions = new(workspace, ocr);
    private readonly ExpeditionOcrService _observations = new(workspace, ocr);

    public async Task RunAsync(
        string route,
        IReadOnlyList<string> selectedItemIds,
        int? areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        if (areasMenuVirtualKey is null)
            throw new InvalidDataException("Areas menu must have a key for shop tasks.");
        ShopItemDefinition[] selected = ShopPurchasePolicy.ValidateSelection(route, selectedItemIds).ToArray();
        ShopKind kind = ShopPurchasePolicy.KindFor(route);
        await OpenShopAsync(kind, areasMenuVirtualKey.Value, reservedVirtualKey, device, status, cancellationToken)
            .ConfigureAwait(false);
        await PurchaseSelectedAsync(kind, selected, device, status, cancellationToken).ConfigureAwait(false);
        status($"{route.ToUpperInvariant()} | RESPAWNING");
        await respawn.RunAsync(
            areasMenuVirtualKey, reservedVirtualKey, device, cancellationToken).ConfigureAwait(false);
        status($"{route.ToUpperInvariant()} | COMPLETE | LOBBY VERIFIED");
    }

    private async Task OpenShopAsync(
        ShopKind kind,
        int areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        status($"{kind.ToString().ToUpperInvariant()} SHOP | OPENING AREAS");
        ObservedStateTransitionRunResult areas = await _transitions.RunAsync(
            DebugWorkflowCatalog.Lobby,
            DebugWorkflowCatalog.AreasUi,
            device,
            token => PressActionAsync(areasMenuVirtualKey, reservedVirtualKey, token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(areas, "Lobby to Areas");

        ObservedStateTransitionRunResult shops = await _transitions.RunAsync(
            DebugWorkflowCatalog.AreasUi,
            DebugWorkflowCatalog.ShopAreas,
            device,
            token => ClickShopCategoryAsync(device, token),
            cancellationToken).ConfigureAwait(false);
        RequireTransition(shops, "Areas to Shop Areas");
        DebugOcrSnapshot shopAreas = shops.Observation.Destination;
        OcrTargetMatch destination = RequiredTarget(
            shopAreas,
            kind == ShopKind.Gold ? "Gold Shop" : "Raid Shop");
        await workspace.ClickRobloxAsync(ClientSize, destination.Region.Bounds.Center, cancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(TeleportDelay, cancellationToken).ConfigureAwait(false);

        DebugStateSpec selector = Selector(kind);
        DebugOcrSnapshot selectorState = await WaitForInteractionAsync(
            selector, reservedVirtualKey, device, cancellationToken).ConfigureAwait(false);
        OcrTargetMatch enter = RequiredTarget(
            selectorState,
            kind == ShopKind.Gold ? "Gold Shop" : "View Shop");
        await workspace.ClickRobloxAsync(ClientSize, enter.Region.Bounds.Center, cancellationToken)
            .ConfigureAwait(false);
        DebugOcrSnapshot opened = await _states.WaitForMatchAsync(
            Shop(kind), device, 16, ObservationDelay, cancellationToken).ConfigureAwait(false);
        if (!opened.Evaluation.IsMatch) throw new InvalidOperationException($"{kind} Shop did not open.");
        status($"{kind.ToString().ToUpperInvariant()} SHOP | VERIFIED");
    }

    private async Task PurchaseSelectedAsync(
        ShopKind kind,
        IReadOnlyList<ShopItemDefinition> selected,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        HashSet<string> pending = selected.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        for (int reset = 0; reset < 2; reset++)
        {
            _ = await RequireShopAsync(kind, device, cancellationToken).ConfigureAwait(false);
            await workspace.ScrollRobloxAsync(
                    ClientSize,
                    ShopPurchasePolicy.CatalogScrollPoint,
                    1200,
                    TimeSpan.FromMilliseconds(300),
                    cancellationToken)
                .ConfigureAwait(false);
            await workspace.HoverRobloxAsync(ClientSize, ShopPurchasePolicy.HoverClearPoint, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        }

        int stagnant = 0;
        HashSet<string> observed = new(StringComparer.Ordinal);
        for (int scan = 0; scan < 24 && pending.Count > 0 && stagnant < 6; scan++)
        {
            DebugOcrSnapshot shop = await RequireShopAsync(kind, device, cancellationToken).ConfigureAwait(false);
            ShopLayoutAnchors anchors = LayoutAnchors(shop, kind);
            IReadOnlyList<OcrTextRegion> catalog = await ObserveCatalogAsync(device, cancellationToken).ConfigureAwait(false);
            List<CatalogCandidate> candidates = ResolveCandidates(selected, catalog);
            int before = observed.Count;
            foreach (CatalogCandidate candidate in candidates)
            {
                observed.Add(candidate.Item.Id);
                if (!pending.Contains(candidate.Item.Id)) continue;
                await TryPurchaseAsync(kind, candidate, anchors, device, status, cancellationToken).ConfigureAwait(false);
                pending.Remove(candidate.Item.Id);
            }
            stagnant = observed.Count == before ? stagnant + 1 : 0;
            if (pending.Count == 0) break;
            await workspace.ScrollRobloxAsync(
                    ClientSize,
                    ShopPurchasePolicy.CatalogScrollPoint,
                    -120,
                    TimeSpan.FromMilliseconds(180),
                    cancellationToken)
                .ConfigureAwait(false);
            await workspace.HoverRobloxAsync(ClientSize, ShopPurchasePolicy.HoverClearPoint, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        }
        foreach (string missed in pending)
            status($"{kind.ToString().ToUpperInvariant()} SHOP | NOT FOUND | {missed}");
    }

    private async Task TryPurchaseAsync(
        ShopKind kind,
        CatalogCandidate candidate,
        ShopLayoutAnchors anchors,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CapturedRgbRegion> button = await workspace.CaptureRgbRegionsAsync(
            ClientSize, [candidate.Button], cancellationToken).ConfigureAwait(false);
        if (button.Count != 1 || !ShopPurchasePolicy.IsAvailableButton(button[0].Image))
        {
            status($"{kind.ToString().ToUpperInvariant()} SHOP | UNAVAILABLE | {candidate.Item.DisplayName}");
            return;
        }
        status($"{kind.ToString().ToUpperInvariant()} SHOP | BUYING | {candidate.Item.DisplayName}");
        await workspace.ClickRobloxAsync(ClientSize, candidate.Button.Center, cancellationToken).ConfigureAwait(false);
        DebugOcrSnapshot dialog = await _states.WaitForMatchAsync(
            DebugWorkflowCatalog.ShopPurchaseDialog, device, 8, ObservationDelay, cancellationToken).ConfigureAwait(false);
        if (!dialog.Evaluation.IsMatch)
        {
            status($"{kind.ToString().ToUpperInvariant()} SHOP | NO BUY DIALOG | {candidate.Item.DisplayName}");
            return;
        }
        await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken).ConfigureAwait(false);
        ShopPurchaseDialogActions actions = DialogActions(await _states.RunAsync(
            DebugWorkflowCatalog.ShopPurchaseDialog, device, cancellationToken).ConfigureAwait(false), anchors);
        await workspace.ClickRobloxAsync(ClientSize, actions.Maximum, cancellationToken).ConfigureAwait(false);
        await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        actions = DialogActions(await _states.RunAsync(
            DebugWorkflowCatalog.ShopPurchaseDialog, device, cancellationToken).ConfigureAwait(false), anchors);
        await workspace.ClickRobloxAsync(ClientSize, actions.Buy, cancellationToken).ConfigureAwait(false);
        await WaitForDialogCloseAsync(kind, device, cancellationToken).ConfigureAwait(false);
        status($"{kind.ToString().ToUpperInvariant()} SHOP | ATTEMPTED | {candidate.Item.DisplayName}");
    }

    private async Task WaitForDialogCloseAsync(
        ShopKind kind,
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            DebugOcrSnapshot dialog = await _states.RunAsync(
                DebugWorkflowCatalog.ShopPurchaseDialog, device, cancellationToken).ConfigureAwait(false);
            if (!dialog.Evaluation.IsMatch)
            {
                await RequireShopAsync(kind, device, cancellationToken).ConfigureAwait(false);
                return;
            }
            await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("The shop purchase dialog did not close before its deadline.");
    }

    private async Task<DebugOcrSnapshot> WaitForInteractionAsync(
        DebugStateSpec selector,
        int reservedVirtualKey,
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            DebugOcrSnapshot state = await _states.RunAsync(selector, device, cancellationToken).ConfigureAwait(false);
            if (state.Evaluation.IsMatch) return state;
            await PressAsync('E', 80, reservedVirtualKey, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"{selector.Name} was not verified before its deadline.");
    }

    private async Task<ObservedStateTransitionActionResult> ClickShopCategoryAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot areas = await _states.RunAsync(
            DebugWorkflowCatalog.AreasUi, device, cancellationToken).ConfigureAwait(false);
        if (!areas.Evaluation.IsMatch) return new(false, "AREAS UI NOT VERIFIED", []);
        OcrTargetMatch shop = AreaSelectionRules.Find(AreaCategory.Shop, areas.Regions)
            ?? throw new InvalidOperationException("Verified Areas UI did not expose Shop.");
        await workspace.ClickRobloxAsync(ClientSize, shop.Region.Bounds.Center, cancellationToken).ConfigureAwait(false);
        return new(true, "SHOP CATEGORY CLICKED", ["SHOP CATEGORY VERIFIED + CLICKED"]);
    }

    private Task<ObservedStateTransitionActionResult> PressActionAsync(
        int virtualKey,
        int reservedVirtualKey,
        CancellationToken cancellationToken) => PressActionCoreAsync(virtualKey, reservedVirtualKey, cancellationToken);

    private async Task<ObservedStateTransitionActionResult> PressActionCoreAsync(
        int virtualKey,
        int reservedVirtualKey,
        CancellationToken cancellationToken)
    {
        await PressAsync(virtualKey, 80, reservedVirtualKey, cancellationToken).ConfigureAwait(false);
        return new(true, "NAVIGATION KEY SENT", ["NAVIGATION KEY SENT"]);
    }

    private Task<IReadOnlyList<OcrTextRegion>> ObserveCatalogAsync(
        string device,
        CancellationToken cancellationToken) => _observations.ObserveAsync(
            ShopPurchasePolicy.CatalogRegion, device, cancellationToken);

    private Task<DebugOcrSnapshot> RequireShopAsync(
        ShopKind kind,
        string device,
        CancellationToken cancellationToken) => RequireStateAsync(Shop(kind), device, cancellationToken);

    private async Task<DebugOcrSnapshot> RequireStateAsync(
        DebugStateSpec state,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(state, device, cancellationToken).ConfigureAwait(false);
        return snapshot.Evaluation.IsMatch
            ? snapshot
            : throw new InvalidOperationException($"{state.Name} was not freshly verified.");
    }

    private static List<CatalogCandidate> ResolveCandidates(
        IReadOnlyList<ShopItemDefinition> selected,
        IReadOnlyList<OcrTextRegion> regions)
    {
        IReadOnlyList<OcrTargetMatch> buys = OcrRuleEngine.FindAllTargets(new OcrTargetRule("Buy", "buy"), regions)
            .Where(match => OcrRuleEngine.Normalize(match.Region.Text).StartsWith("buy", StringComparison.Ordinal))
            .ToArray();
        List<CatalogCandidate> resolved = [];
        foreach (ShopItemDefinition item in selected)
        {
            OcrTargetMatch? label = OcrRuleEngine.FindTarget(
                new OcrTargetRule(item.DisplayName, item.OcrAliases.ToArray()), regions);
            if (label is null) continue;
            OcrTargetMatch? buy = buys
                .Where(candidate => candidate.Region.Bounds.Center.Y > label.Region.Bounds.Center.Y &&
                                    Math.Abs(candidate.Region.Bounds.Center.X - label.Region.Bounds.Center.X) < 130)
                .OrderBy(candidate => candidate.Region.Bounds.Center.Y - label.Region.Bounds.Center.Y)
                .FirstOrDefault();
            if (buy is null) continue;
            PixelPoint center = buy.Region.Bounds.Center;
            PixelRect button = new(center.X - 75, center.Y - 22, 150, 44);
            if (button.IsInside(ClientSize)) resolved.Add(new CatalogCandidate(item, button));
        }
        return resolved;
    }

    private static ShopPurchaseDialogActions DialogActions(
        DebugOcrSnapshot dialog,
        ShopLayoutAnchors anchors)
    {
        if (!dialog.Evaluation.IsMatch)
            throw new InvalidOperationException("Shop purchase dialog was not freshly verified.");
        PixelRect cancel = RequiredTarget(dialog, "Cancel").Region.Bounds;
        return ShopPurchasePolicy.TryResolveDialogActions(
            anchors.Primary,
            anchors.Secondary,
            cancel,
            ClientSize,
            out ShopPurchaseDialogActions actions)
            ? actions
            : throw new InvalidOperationException("The live shop purchase dialog was not safe to use.");
    }

    private Task PressAsync(int key, int hold, int reserved, CancellationToken token) =>
        workspace.RunKeySequenceAsync(ClientSize, AutomationKeySequence.Create(
            [AutomationKeyPress.Create(key, hold, reserved)]), token);

    private static DebugStateSpec Selector(ShopKind kind) => kind == ShopKind.Gold
        ? DebugWorkflowCatalog.GoldShopSelector
        : DebugWorkflowCatalog.RaidShopSelector;

    private static DebugStateSpec Shop(ShopKind kind) => kind == ShopKind.Gold
        ? DebugWorkflowCatalog.GoldShop
        : DebugWorkflowCatalog.RaidShop;

    private static ShopLayoutAnchors LayoutAnchors(DebugOcrSnapshot shop, ShopKind kind) => kind switch
    {
        ShopKind.Gold => new(
            BottommostTarget(shop, new OcrTargetRule("Gold Shop", "gold shop", "goldshop")),
            RequiredTarget(shop, "Cosmetic Shop").Region.Bounds),
        ShopKind.Raid => new(
            BottommostTarget(shop, new OcrTargetRule("General", "general")),
            RequiredTarget(shop, "Spirit City").Region.Bounds),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static PixelRect BottommostTarget(DebugOcrSnapshot state, OcrTargetRule rule) =>
        OcrRuleEngine.FindAllTargets(rule, state.Regions)
            .OrderByDescending(match => match.Region.Bounds.Center.Y)
            .Select(match => match.Region.Bounds)
            .FirstOrDefault() is { Width: > 0, Height: > 0 } bounds
                ? bounds
                : throw new InvalidOperationException($"Verified {state.State} did not expose {rule.Name}.");

    private static OcrTargetMatch RequiredTarget(DebugOcrSnapshot state, string name) =>
        state.Evaluation.Matches.FirstOrDefault(match => match.Target == name)
        ?? throw new InvalidOperationException($"Verified {state.State} did not expose {name}.");

    private static void RequireTransition(ObservedStateTransitionRunResult result, string name)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{name} transition failed after {result.ActionAttempts} action attempt(s).");
    }

    private sealed record CatalogCandidate(ShopItemDefinition Item, PixelRect Button);

    private readonly record struct ShopLayoutAnchors(PixelRect Primary, PixelRect Secondary);
}
