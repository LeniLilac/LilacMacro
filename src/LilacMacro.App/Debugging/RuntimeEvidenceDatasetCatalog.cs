namespace LilacMacro.App.Debugging;

internal static class RuntimeEvidenceDatasetCatalog
{
    internal const string RelativeRoot = "Assets/RuntimeEvidence";

    public static string Dataset(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            throw new ArgumentException("Runtime evidence dataset names cannot contain a path.", nameof(name));

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string repository = Path.Combine(
                directory.FullName, "src", "LilacMacro.App", "Assets", "RuntimeEvidence", name);
            if (Directory.Exists(repository)) return repository;
            directory = directory.Parent;
        }

        string installed = Path.Combine(AppContext.BaseDirectory, "Assets", "RuntimeEvidence", name);
        if (Directory.Exists(installed)) return installed;

        return installed;
    }
}
