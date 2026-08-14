using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Runtime;

internal sealed record ExpeditionRewardObservation(
    ExpeditionRewardPool Pool,
    PixelPoint BackPoint,
    IReadOnlyList<string> OcrText);

internal sealed class ExpeditionRewardPoolService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly PixelRect FullClient = new(0, 0, 1366, 700);
    private static readonly PixelRect RewardStrip = new(0, 545, 820, 155);
    private readonly ExpeditionOcrService _ocr = new(workspace, ocr);
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private PixelPoint? _mapPoint;

    public async Task OpenAsync(string device, CancellationToken cancellationToken)
    {
        DebugRunReport prestart = await _debug.CheckMatchPrestartAsync(device, cancellationToken)
            .ConfigureAwait(false);
        if (!prestart.Succeeded)
            throw new InvalidOperationException("Expedition Map requires verified match prestart.");

        for (int attempt = 0; attempt < 8; attempt++)
        {
            IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
                FullClient, device, cancellationToken).ConfigureAwait(false);
            OcrTextRegion? map = regions.FirstOrDefault(region => Normalize(region.Text) == "expeditionmap");
            int menuAnchors = regions.Count(region => Normalize(region.Text) is
                "expeditionmap" or "teamloadout" or "startgame");
            if (map is not null && menuAnchors >= 2)
            {
                _mapPoint = map.Bounds.Center;
                await OpenAtCachedPointUntilBackAsync(device, cancellationToken).ConfigureAwait(false);
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Verified Expedition Map action was not found.");
    }

    public async Task OpenAfterRestartAsync(string device, CancellationToken cancellationToken)
    {
        await OpenAtCachedPointUntilBackAsync(device, cancellationToken).ConfigureAwait(false);
    }

    private async Task OpenAtCachedPointUntilBackAsync(
        string device,
        CancellationToken cancellationToken)
    {
        PixelPoint point = _mapPoint ??
            throw new InvalidOperationException("Expedition Map did not have an operation-scoped live target.");
        for (int attempt = 0; attempt < 18; attempt++)
        {
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize, point, cancellationToken).ConfigureAwait(false);
            if (await IsBackVisibleAsync(device, cancellationToken).ConfigureAwait(false)) return;
        }
        throw new InvalidOperationException("Expedition Map did not open after the restart transition.");
    }

    public async Task<ExpeditionRewardObservation> ObserveAsync(
        ExpeditionRewardResource target,
        string device,
        CancellationToken cancellationToken)
    {
        ExpeditionRewardPool? previousPool = null;
        int stableObservations = 0;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
                RewardStrip, device, cancellationToken, scale: 4).ConfigureAwait(false);
            OcrTextRegion? back = regions.FirstOrDefault(region => Normalize(region.Text) == "back");
            bool routeRewards = ContainsPhrase(regions, "routerewards");
            bool parsed = TryPoolFromRegions(regions, out ExpeditionRewardPool pool);
            if (back is not null && routeRewards && HasPopulatedRewardStrip(regions) && parsed)
            {
                stableObservations = previousPool is not null && PoolsEqual(previousPool, pool)
                    ? stableObservations + 1
                    : 1;
                previousPool = pool;
                if (stableObservations >= 2)
                {
                    return new ExpeditionRewardObservation(
                        pool,
                        back.Bounds.Center,
                        regions.Select(region => region.Text).ToArray());
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"Expedition route reward '{target}' was not read reliably.");
    }

    public async Task BackToPrestartAsync(
        ExpeditionRewardObservation observation,
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            DebugRunReport prestart = await _debug.CheckMatchPrestartAsync(device, cancellationToken)
                .ConfigureAwait(false);
            if (prestart.Succeeded) return;
            if (await IsBackVisibleAsync(device, cancellationToken).ConfigureAwait(false))
            {
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, observation.BackPoint, cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Route Rewards did not return to verified match prestart.");
    }

    public async Task BackToPrestartAfterReadFailureAsync(
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            DebugRunReport prestart = await _debug.CheckMatchPrestartAsync(device, cancellationToken)
                .ConfigureAwait(false);
            if (prestart.Succeeded) return;
            IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
                RewardStrip, device, cancellationToken, scale: 4).ConfigureAwait(false);
            OcrTextRegion? back = regions.FirstOrDefault(region => Normalize(region.Text) == "back");
            if (back is not null)
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, back.Bounds.Center, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Route Rewards recovery did not reach verified match prestart.");
    }

    public async Task StartGameForRouteAsync(
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            DebugRunReport prestart = await _debug.CheckMatchPrestartAsync(device, cancellationToken)
                .ConfigureAwait(false);
            if (!prestart.Succeeded) return;
            DebugRunReport start = await _debug.StartGameAsync(device, cancellationToken)
                .ConfigureAwait(false);
            if (!start.Succeeded)
                throw new InvalidOperationException("Verified match prestart did not expose Start Game.");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Start Game remained at match prestart after bounded retries.");
    }

    private async Task<bool> IsBackVisibleAsync(string device, CancellationToken cancellationToken)
    {
        IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
            RewardStrip, device, cancellationToken, scale: 4).ConfigureAwait(false);
        return HasBackButton(regions);
    }

    internal static bool HasBackButton(IReadOnlyList<OcrTextRegion> regions) =>
        regions.Any(region => Normalize(region.Text) == "back");

    internal static (ExpeditionRewardResource Resource, int Quantity)? FindReward(
        IReadOnlyList<OcrTextRegion> regions,
        ExpeditionRewardResource target)
    {
        if (target == ExpeditionRewardResource.None) return (target, 0);
        IReadOnlyDictionary<ExpeditionRewardResource, OcrTextRegion> cards = AssociateRewardCards(regions);
        if (cards.TryGetValue(target, out OcrTextRegion? quantity) &&
            ExpeditionRewardPolicy.ParseQuantity(quantity.Text, target) is int parsed)
            return (target, parsed);
        return null;
    }

    internal static ExpeditionRewardPool PoolForObservation(
        ExpeditionRewardResource target,
        (ExpeditionRewardResource Resource, int Quantity)? reward) =>
        new(new Dictionary<ExpeditionRewardResource, int>
        {
            [target] = reward?.Quantity ?? 0,
        });

    internal static ExpeditionRewardPool PoolFromRegions(IReadOnlyList<OcrTextRegion> regions)
    {
        _ = TryPoolFromRegions(regions, out ExpeditionRewardPool pool);
        return pool;
    }

    internal static bool TryPoolFromRegions(
        IReadOnlyList<OcrTextRegion> regions,
        out ExpeditionRewardPool pool)
    {
        IReadOnlyDictionary<ExpeditionRewardResource, OcrTextRegion> cards = AssociateRewardCards(regions);
        Dictionary<ExpeditionRewardResource, int> quantities = [];
        bool complete = true;
        foreach (ExpeditionRewardResource resource in Enum.GetValues<ExpeditionRewardResource>()
                     .Where(resource => resource != ExpeditionRewardResource.None))
        {
            if (!cards.TryGetValue(resource, out OcrTextRegion? quantity))
            {
                quantities[resource] = 0;
                continue;
            }
            int? parsed = ExpeditionRewardPolicy.ParseQuantity(quantity.Text, resource);
            complete &= parsed.HasValue;
            quantities[resource] = parsed ?? 0;
        }
        pool = new ExpeditionRewardPool(quantities);
        return complete;
    }

    internal static bool HasPopulatedRewardStrip(IReadOnlyList<OcrTextRegion> regions) =>
        regions.Count(region => IsQuantity(region.Text)) >= 4;

    private static bool PoolsEqual(ExpeditionRewardPool left, ExpeditionRewardPool right) =>
        Enum.GetValues<ExpeditionRewardResource>()
            .Where(resource => resource != ExpeditionRewardResource.None)
            .All(resource => left.Quantity(resource) == right.Quantity(resource));

    internal static IReadOnlyDictionary<ExpeditionRewardResource, OcrTextRegion> AssociateRewardCards(
        IReadOnlyList<OcrTextRegion> regions)
    {
        OcrTextRegion[] quantities = regions
            .Where(region => IsQuantity(region.Text) && region.Bounds.Center.Y < 630)
            .OrderBy(region => region.Bounds.Center.X)
            .ToArray();
        if (quantities.Length < 4) return new Dictionary<ExpeditionRewardResource, OcrTextRegion>();
        int typicalSpacing = quantities.Length > 1
            ? (int)Math.Round(quantities.Zip(quantities.Skip(1),
                (left, right) => right.Bounds.Center.X - left.Bounds.Center.X).Average())
            : 75;
        int ownershipPadding = Math.Clamp(typicalSpacing / 3, 8, 24);
        Dictionary<int, List<OcrTextRegion>> labels = [];
        foreach (OcrTextRegion region in regions.Where(region => region.Bounds.Center.Y is >= 640 and <= 690))
        {
            int index = Array.FindLastIndex(quantities,
                quantity => quantity.Bounds.Center.X <= region.Bounds.X + ownershipPadding);
            if (index < 0) continue;
            if (index + 1 < quantities.Length &&
                region.Bounds.X >= quantities[index + 1].Bounds.Center.X + ownershipPadding)
            {
                continue;
            }
            if (!labels.TryGetValue(index, out List<OcrTextRegion>? parts)) labels[index] = parts = [];
            parts.Add(region);
        }
        Dictionary<ExpeditionRewardResource, OcrTextRegion> cards = [];
        foreach ((int index, List<OcrTextRegion> parts) in labels)
        {
            string label = string.Concat(parts
                .OrderBy(region => region.Bounds.Center.Y)
                .ThenBy(region => region.Bounds.X)
                .Select(region => Normalize(region.Text)));
            ExpeditionRewardResource? resource = Identify(label);
            if (resource is not null && !cards.ContainsKey(resource.Value)) cards[resource.Value] = quantities[index];
        }
        return cards;
    }

    internal static ExpeditionRewardResource? Identify(string label)
    {
        ExpeditionRewardResource[] resources =
        [
            ExpeditionRewardResource.FuelCell,
            ExpeditionRewardResource.EquipmentScrap,
            ExpeditionRewardResource.EquipmentReroll,
            ExpeditionRewardResource.EquipmentLock,
            ExpeditionRewardResource.ExpeditionCoin,
        ];
        return resources
            .Select(resource => (Resource: resource, Match: LabelMatch(label, resource)))
            .Where(candidate => candidate.Match.IsMatch)
            .OrderByDescending(candidate => candidate.Match.Similarity)
            .Select(candidate => (ExpeditionRewardResource?)candidate.Resource)
            .FirstOrDefault();
    }

    private static OcrPhraseMatchResult LabelMatch(string label, ExpeditionRewardResource resource) => resource switch
    {
        ExpeditionRewardResource.FuelCell => OcrPhraseMatcher.Match(
            "fuelcell", label, OcrMatchMode.FuzzyPhrase, 0.75),
        ExpeditionRewardResource.EquipmentScrap => OcrPhraseMatcher.Match(
            "equipmentscrap", label, OcrMatchMode.FuzzyPhrase, 0.78),
        ExpeditionRewardResource.EquipmentReroll when
            label.Contains("rerdl", StringComparison.Ordinal) ||
            label.Contains("rerdll", StringComparison.Ordinal) =>
            new OcrPhraseMatchResult(true, 0.99, "equipmentreroll", label),
        ExpeditionRewardResource.EquipmentReroll => OcrPhraseMatcher.Match(
            "equipmentreroll", label, OcrMatchMode.FuzzyPhrase, 0.7),
        ExpeditionRewardResource.EquipmentLock => OcrPhraseMatcher.Match(
            "equipmentlock", label, OcrMatchMode.FuzzyPhrase, 0.78),
        ExpeditionRewardResource.ExpeditionCoin => OcrPhraseMatcher.Match(
            "expeditioncoin", label, OcrMatchMode.FuzzyPhrase, 0.78),
        _ => new OcrPhraseMatchResult(false, 0, string.Empty, Normalize(label)),
    };

    private static bool IsQuantity(string value) => Normalize(value).EndsWith('x') || Normalize(value).EndsWith('c');

    private static bool ContainsPhrase(IEnumerable<OcrTextRegion> regions, string expected) =>
        Normalize(string.Join(' ', regions.Select(region => region.Text))).Contains(expected, StringComparison.Ordinal);

    private static string Normalize(string value) => new(
        value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
