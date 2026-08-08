namespace LilacMacro.Core.Placements;

public sealed record PlacementMapDefinition(
    string Id,
    PlacementMapMode Mode,
    string DisplayName,
    IReadOnlyList<string> DatasetNames);
