using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public sealed class PlacementEditorSession
{
    private readonly PlacementSetupStore _store;
    private readonly object _saveSync = new();
    private Task _pendingSave = Task.CompletedTask;

    public PlacementEditorSession(PlacementSetupStore store)
    {
        _store = store;
    }

    public PlacementSetupDocument? Document { get; private set; }

    public PlacementMapDefinition? Map { get; private set; }

    public IReadOnlyList<PlacementRouteDefinition> Routes { get; private set; } = [];

    public PlacementRouteDefinition? SelectedRoute { get; private set; }

    public PlacementRouteSetup CurrentRoute => Document is null || SelectedRoute is null
        ? throw new InvalidOperationException("No placement route is open.")
        : PlacementRouteCatalog.EffectiveRoute(Document, SelectedRoute);

    public bool UsesShared => Document is not null && SelectedRoute is not null &&
        PlacementRouteCatalog.UsesShared(Document, SelectedRoute);

    public bool CanEdit => SelectedRoute is not null;

    public bool CanReset => Document is not null && SelectedRoute is not null &&
        (SelectedRoute.IsShared
            ? Document.Shared.Steps.Any(step => step.Kind != PlacementStepKind.StartGame)
            : Document.Overrides.ContainsKey(SelectedRoute.Id));

    public async Task OpenAsync(
        PlacementMapDefinition map,
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken = default)
    {
        await FlushAsync().ConfigureAwait(false);
        Document = await _store.LoadOrCreateAsync(
            map.Id,
            imageWidth,
            imageHeight,
            cancellationToken).ConfigureAwait(false);
        Map = map;
        Routes = PlacementRouteCatalog.For(map);
        SelectedRoute = Routes[0];
    }

    public void SelectRoute(string routeId)
    {
        SelectedRoute = Routes.FirstOrDefault(route =>
            string.Equals(route.Id, routeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Placement route is not available for this map.", nameof(routeId));
    }

    public Task CustomizeAsync()
    {
        EnsureOpen();
        if (SelectedRoute!.IsShared || !UsesShared) return Task.CompletedTask;
        return MutateAsync(document =>
        {
            document.Overrides.Add(
                SelectedRoute.Id,
                PlacementSetupRules.CloneRoute(document.Shared, SelectedRoute.Id));
        });
    }

    public Task UseSharedAsync()
    {
        EnsureOpen();
        if (SelectedRoute!.IsShared || UsesShared) return Task.CompletedTask;
        return MutateAsync(document => document.Overrides.Remove(SelectedRoute.Id));
    }

    public Task ResetAsync()
    {
        EnsureOpen();
        return MutateAsync(document =>
        {
            if (SelectedRoute!.IsShared)
            {
                document.Shared.Steps = [PlacementStep.CreateStartGame()];
            }
            else
            {
                document.Overrides.Remove(SelectedRoute.Id);
            }
        });
    }

    public async Task CopyFromAsync(
        PlacementMapDefinition sourceMap,
        PlacementRouteDefinition sourceRoute,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceMap);
        ArgumentNullException.ThrowIfNull(sourceRoute);
        EnsureOpen();
        await FlushAsync().ConfigureAwait(false);
        PlacementSetupDocument sourceDocument = await _store.LoadAsync(sourceMap.Id, cancellationToken).ConfigureAwait(false);
        PlacementRouteSetup sourceSetup = PlacementRouteCatalog.EffectiveRoute(sourceDocument, sourceRoute);
        PlacementRouteSetup copy = PlacementSetupRules.CopyRouteToSurface(
            sourceSetup,
            SelectedRoute!.Id,
            sourceDocument.ImageWidth,
            sourceDocument.ImageHeight,
            targetWidth,
            targetHeight);
        await MutateRouteAsync(route =>
        {
            route.TeamSlot = copy.TeamSlot;
            route.SelectedUnitSlot = copy.SelectedUnitSlot;
            route.BetweenUpgradeAttemptsMilliseconds = copy.BetweenUpgradeAttemptsMilliseconds;
            route.DefaultTargetingPriority = copy.DefaultTargetingPriority;
            route.DefaultAutoUpgradePriority = copy.DefaultAutoUpgradePriority;
            route.Steps = copy.Steps;
        }).ConfigureAwait(false);
    }

    public Task SetRouteDefaultsAsync(
        int teamSlot,
        int selectedUnitSlot,
        PlacementTargetingPriority targeting,
        PlacementAutoUpgradePriority autoUpgrade) => MutateRouteAsync(route =>
        {
            route.TeamSlot = teamSlot;
            route.SelectedUnitSlot = selectedUnitSlot;
            route.DefaultTargetingPriority = targeting;
            route.DefaultAutoUpgradePriority = autoUpgrade;
        });

    public Task AddPlacementAsync(int x, int y, PlacementStep? template = null) => MutateRouteAsync(route =>
    {
        PlacementStep source = template ?? PlacementStep.CreatePlace(
            route.SelectedUnitSlot,
            x,
            y,
            route.DefaultTargetingPriority,
            route.DefaultAutoUpgradePriority);
        if (source.Kind != PlacementStepKind.Place)
        {
            throw new ArgumentException("Placement template must be a Place step.", nameof(template));
        }
        route.Steps.Add(source with { Id = Guid.NewGuid(), X = x, Y = y });
    });

    public Task AddStepAsync(PlacementStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Kind is PlacementStepKind.Place or PlacementStepKind.StartGame)
        {
            throw new ArgumentException("This step must be added through its dedicated flow.", nameof(step));
        }
        return MutateRouteAsync(route => route.Steps.Add(step with { Id = Guid.NewGuid() }));
    }

    public Task AddDelayAsync() => MutateRouteAsync(route => route.Steps.Add(new PlacementStep
    {
        Kind = PlacementStepKind.Delay,
        DelayDurationMilliseconds = 1_000,
    }));

    public Task AddUnitActionAsync(PlacementStepKind kind, Guid targetPlacementId) => MutateRouteAsync(route =>
    {
        if (kind is not (PlacementStepKind.Reconfigure or PlacementStepKind.Upgrade or PlacementStepKind.Sell))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        PlacementStep target = route.Steps.FirstOrDefault(step => step.Id == targetPlacementId &&
            step.Kind == PlacementStepKind.Place)
            ?? throw new InvalidOperationException("Select an existing placement first.");
        route.Steps.Add(new PlacementStep
        {
            Kind = kind,
            TargetPlacementId = target.Id,
            UnitSlot = target.UnitSlot,
            ChangeTargetingPriority = kind == PlacementStepKind.Reconfigure,
            TargetingPriority = PlacementTargetingPriority.Last,
            UpgradeCount = kind == PlacementStepKind.Upgrade ? 1 : 0,
        });
    });

    public Task SetBetweenUpgradeAttemptsAsync(int milliseconds) => MutateRouteAsync(route =>
    {
        PlacementSetupRules.ValidateActionDelay(milliseconds);
        route.BetweenUpgradeAttemptsMilliseconds = milliseconds;
    });

    public Task ReplaceStepAsync(int index, PlacementStep replacement) => MutateRouteAsync(route =>
    {
        if (index < 0 || index >= route.Steps.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (route.Steps[index].Kind == PlacementStepKind.StartGame || replacement.Kind == PlacementStepKind.StartGame)
        {
            throw new InvalidOperationException("Start Game is edited by moving it in the timeline.");
        }
        route.Steps[index] = replacement with { Id = route.Steps[index].Id };
    });

    public Task MovePlacementAsync(Guid placementId, int x, int y) => MutateRouteAsync(route =>
    {
        int index = route.Steps.FindIndex(step =>
            step.Id == placementId && step.Kind == PlacementStepKind.Place);
        if (index < 0)
        {
            throw new InvalidOperationException("Placement is no longer available.");
        }

        route.Steps[index] = route.Steps[index] with { X = x, Y = y };
    });

    public Task DeleteStepAsync(int index) => MutateRouteAsync(route =>
    {
        if (index < 0 || index >= route.Steps.Count) throw new ArgumentOutOfRangeException(nameof(index));
        PlacementStep target = route.Steps[index];
        if (target.Kind == PlacementStepKind.StartGame)
        {
            throw new InvalidOperationException("Start Game is required.");
        }
        if (target.Kind == PlacementStepKind.Place)
        {
            route.Steps.RemoveAll(step => step.Id == target.Id || step.TargetPlacementId == target.Id);
        }
        else
        {
            route.Steps.RemoveAt(index);
        }
    });

    public Task MoveStepToAsync(int index, int destination) => MutateRouteAsync(route =>
    {
        if (index < 0 || index >= route.Steps.Count ||
            destination < 0 || destination >= route.Steps.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (index == destination) return;
        PlacementStep step = route.Steps[index];
        route.Steps.RemoveAt(index);
        route.Steps.Insert(destination, step);
    });

    public Task FlushAsync()
    {
        lock (_saveSync) return _pendingSave;
    }

    public (PlacementSetupDocument Document, PlacementRouteSetup Route) CreatePlaybackSnapshot()
    {
        EnsureOpen();
        return (
            PlacementSetupRules.CloneDocument(Document!),
            PlacementSetupRules.CloneRoute(CurrentRoute, CurrentRoute.RouteId));
    }

    private Task MutateRouteAsync(Action<PlacementRouteSetup> mutation)
    {
        EnsureOpen();
        return MutateAsync(document =>
        {
            PlacementRouteSetup route;
            if (SelectedRoute!.IsShared)
            {
                route = document.Shared;
            }
            else if (!document.Overrides.TryGetValue(SelectedRoute.Id, out route!))
            {
                route = PlacementSetupRules.CloneRoute(document.Shared, SelectedRoute.Id);
                document.Overrides.Add(SelectedRoute.Id, route);
            }
            mutation(route);
        });
    }

    private Task MutateAsync(Action<PlacementSetupDocument> mutation)
    {
        EnsureOpen();
        PlacementSetupDocument before = PlacementSetupRules.CloneDocument(Document!);
        try
        {
            mutation(Document!);
            PlacementSetupRules.Validate(Document!);
        }
        catch
        {
            Document = before;
            throw;
        }

        PlacementSetupDocument snapshot = PlacementSetupRules.CloneDocument(Document!);
        lock (_saveSync)
        {
            Task previous = _pendingSave;
            _pendingSave = PersistAfterAsync(previous, snapshot);
            return _pendingSave;
        }
    }

    private async Task PersistAfterAsync(Task previous, PlacementSetupDocument snapshot)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A newer valid snapshot still gets one independent save attempt.
        }
        await _store.SaveAsync(snapshot).ConfigureAwait(false);
    }

    private void EnsureOpen()
    {
        if (Document is null || SelectedRoute is null) throw new InvalidOperationException("No placement map is open.");
    }

}
