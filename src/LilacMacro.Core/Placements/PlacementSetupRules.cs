namespace LilacMacro.Core.Placements;

public static class PlacementSetupRules
{
    public const int DefaultStepDelayMilliseconds = 900;
    public const int MaximumStepDelayMilliseconds = 60_000;
    public const int MaximumDelayDurationMilliseconds = 3_600_000;
    public const int MaximumUpgradeCount = 100;
    public const int MinimumPlacementSpacingPixels = 7;

    public static PlacementSetupDocument CreateDocument(string mapId, int imageWidth, int imageHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        return new PlacementSetupDocument
        {
            MapId = mapId,
            ImageWidth = imageWidth,
            ImageHeight = imageHeight,
            Shared = CreateRoute(PlacementRouteCatalog.SharedRouteId),
        };
    }

    public static PlacementRouteSetup CreateRoute(string routeId) => new()
    {
        RouteId = routeId,
    };

    public static PlacementRouteSetup CloneRoute(PlacementRouteSetup source, string routeId)
    {
        ArgumentNullException.ThrowIfNull(source);
        Dictionary<Guid, Guid> ids = source.Steps.ToDictionary(step => step.Id, _ => Guid.NewGuid());
        return new PlacementRouteSetup
        {
            RouteId = routeId,
            TeamSlot = source.TeamSlot,
            SelectedUnitSlot = source.SelectedUnitSlot,
            DefaultStepDelayMilliseconds = source.DefaultStepDelayMilliseconds,
            DefaultTargetingPriority = source.DefaultTargetingPriority,
            DefaultAutoUpgradePriority = source.DefaultAutoUpgradePriority,
            Steps = source.Steps.Select(step => step with
            {
                Id = ids[step.Id],
                TargetPlacementId = step.TargetPlacementId is Guid target ? ids[target] : null,
            }).ToList(),
        };
    }

    public static PlacementSetupDocument CloneDocument(PlacementSetupDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PlacementSetupDocument clone = new()
        {
            SchemaVersion = source.SchemaVersion,
            MapId = source.MapId,
            ImageWidth = source.ImageWidth,
            ImageHeight = source.ImageHeight,
            Shared = CloneRoutePreservingIds(source.Shared),
        };
        foreach ((string key, PlacementRouteSetup route) in source.Overrides)
        {
            clone.Overrides.Add(key, CloneRoutePreservingIds(route));
        }
        return clone;
    }

    public static void Validate(PlacementSetupDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != PlacementSetupDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException("Placement setup schema is unsupported.");
        }
        if (!IsSafeId(document.MapId)) throw new InvalidDataException("Placement map id is invalid.");
        if (document.ImageWidth is < 1 or > 16_384 || document.ImageHeight is < 1 or > 16_384)
        {
            throw new InvalidDataException("Placement image size is invalid.");
        }
        if (!string.Equals(document.Shared.RouteId, PlacementRouteCatalog.SharedRouteId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Shared placement route is invalid.");
        }
        ValidateRoute(document.Shared, document.ImageWidth, document.ImageHeight);
        foreach ((string routeId, PlacementRouteSetup route) in document.Overrides)
        {
            if (!IsSafeId(routeId) || !string.Equals(routeId, route.RouteId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Placement route override id is invalid.");
            }
            if (string.Equals(routeId, PlacementRouteCatalog.SharedRouteId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Shared route cannot be stored as an override.");
            }
            ValidateRoute(route, document.ImageWidth, document.ImageHeight);
        }
    }

    public static void ValidateRoute(PlacementRouteSetup route, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!IsSafeId(route.RouteId)) throw new InvalidDataException("Placement route id is invalid.");
        if (route.TeamSlot is < 1 or > 8 || route.SelectedUnitSlot is < 1 or > 6)
        {
            throw new InvalidDataException("Team slot must be 1 through 8 and unit slot must be 1 through 6.");
        }
        ValidateStepDelay(route.DefaultStepDelayMilliseconds);
        if (!Enum.IsDefined(route.DefaultTargetingPriority) || !Enum.IsDefined(route.DefaultAutoUpgradePriority))
        {
            throw new InvalidDataException("Placement route defaults are invalid.");
        }
        if (route.Steps.Count(step => step.Kind == PlacementStepKind.StartGame) != 1)
        {
            throw new InvalidDataException("Placement timeline requires exactly one Start Game step.");
        }
        if (route.Steps.Select(step => step.Id).Any(id => id == Guid.Empty) ||
            route.Steps.Select(step => step.Id).Distinct().Count() != route.Steps.Count)
        {
            throw new InvalidDataException("Placement step ids must be unique.");
        }

        Dictionary<Guid, PlacementStep> priorPlacements = [];
        foreach (PlacementStep step in route.Steps)
        {
            ValidateStep(step, imageWidth, imageHeight, priorPlacements);
            if (step.Kind == PlacementStepKind.Place) priorPlacements.Add(step.Id, step);
        }
        ValidateSpacing(priorPlacements.Values);
    }

    public static void ValidateStepDelay(int milliseconds)
    {
        if (milliseconds is < 0 or > MaximumStepDelayMilliseconds)
        {
            throw new InvalidDataException($"Step delay must be 0 through {MaximumStepDelayMilliseconds} ms.");
        }
    }

    private static void ValidateStep(
        PlacementStep step,
        int imageWidth,
        int imageHeight,
        IReadOnlyDictionary<Guid, PlacementStep> priorPlacements)
    {
        if (!Enum.IsDefined(step.Kind) || !Enum.IsDefined(step.TargetingPriority) ||
            !Enum.IsDefined(step.AutoUpgradePriority) || !Enum.IsDefined(step.AutoUpgradeAction))
        {
            throw new InvalidDataException("Placement step options are invalid.");
        }
        ValidateStepDelay(step.DelayAfterMilliseconds);
        if (step.Kind == PlacementStepKind.Place)
        {
            if (step.UnitSlot is < 1 or > 6 || step.X < 0 || step.X >= imageWidth || step.Y < 0 || step.Y >= imageHeight)
            {
                throw new InvalidDataException("Placement coordinate or unit slot is invalid.");
            }
            if (step.TargetPlacementId is not null) throw new InvalidDataException("Place step cannot reference another placement.");
            return;
        }
        if (step.Kind is PlacementStepKind.Reconfigure or PlacementStepKind.Upgrade or PlacementStepKind.Sell)
        {
            if (step.TargetPlacementId is not Guid target || !priorPlacements.ContainsKey(target))
            {
                throw new InvalidDataException("Unit action must reference an earlier placement.");
            }
        }
        if (step.Kind == PlacementStepKind.Reconfigure &&
            !step.ChangeTargetingPriority && step.AutoUpgradeAction == PlacementAutoUpgradeAction.NoChange)
        {
            throw new InvalidDataException("Reconfigure must change targeting or Auto Upgrade.");
        }
        if (step.Kind == PlacementStepKind.Delay &&
            step.DelayDurationMilliseconds is < 1 or > MaximumDelayDurationMilliseconds)
        {
            throw new InvalidDataException("Delay duration is invalid.");
        }
        if (step.Kind == PlacementStepKind.Upgrade && step.UpgradeCount is < 1 or > MaximumUpgradeCount)
        {
            throw new InvalidDataException("Upgrade count is invalid.");
        }
    }

    private static void ValidateSpacing(IEnumerable<PlacementStep> placements)
    {
        PlacementStep[] items = placements.ToArray();
        for (int first = 0; first < items.Length; first++)
        {
            for (int second = first + 1; second < items.Length; second++)
            {
                long deltaX = items[first].X - items[second].X;
                long deltaY = items[first].Y - items[second].Y;
                if (deltaX * deltaX + deltaY * deltaY < MinimumPlacementSpacingPixels * MinimumPlacementSpacingPixels)
                {
                    throw new InvalidDataException($"Placements must be at least {MinimumPlacementSpacingPixels} pixels apart.");
                }
            }
        }
    }

    private static PlacementRouteSetup CloneRoutePreservingIds(PlacementRouteSetup source) => new()
    {
        RouteId = source.RouteId,
        TeamSlot = source.TeamSlot,
        SelectedUnitSlot = source.SelectedUnitSlot,
        DefaultStepDelayMilliseconds = source.DefaultStepDelayMilliseconds,
        DefaultTargetingPriority = source.DefaultTargetingPriority,
        DefaultAutoUpgradePriority = source.DefaultAutoUpgradePriority,
        Steps = source.Steps.Select(step => step with { }).ToList(),
    };

    private static bool IsSafeId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
