namespace LilacMacro.Core.Placements;

public sealed record PlacementMapReference(
    PlacementMapDefinition Definition,
    IReadOnlyList<string> DatasetDirectories,
    IReadOnlyList<string> ImagePaths,
    int ImageWidth,
    int ImageHeight);
