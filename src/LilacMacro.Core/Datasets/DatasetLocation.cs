namespace LilacMacro.Core.Datasets;

public sealed record DatasetLocation(string DirectoryPath, DatasetManifest Manifest)
{
    public string ManifestPath => Path.Combine(DirectoryPath, DatasetStore.ManifestFileName);

    public string ImagesPath => Path.Combine(DirectoryPath, DatasetStore.ImagesDirectoryName);
}
