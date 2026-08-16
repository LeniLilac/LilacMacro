namespace LilacMacro.App.Infrastructure;

internal sealed class ConfigurationMutationGate
{
    private readonly string _path;

    internal ConfigurationMutationGate(string configurationRoot)
    {
        string canonical = Path.GetFullPath(configurationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _path = Path.Combine(canonical, ".configuration-ownership.lock");
    }

    internal string LockPath => _path;

    public static ConfigurationMutationGate CreateDefault() =>
        new(MacroInstanceContext.Current.ConfigurationRoot);

    public IDisposable AcquireRunLease() => Acquire(readOnly: true);

    public IDisposable AcquireMutationLease() => Acquire(readOnly: false);

    private FileStream Acquire(bool readOnly)
    {
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Configuration ownership path is invalid.");
        Directory.CreateDirectory(directory);
        try
        {
            if (!File.Exists(_path))
            {
                using FileStream seed = new(
                    _path,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    1);
            }
            return new FileStream(
                _path,
                FileMode.Open,
                readOnly ? FileAccess.Read : FileAccess.ReadWrite,
                readOnly ? FileShare.Read : FileShare.None,
                1);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                readOnly
                    ? "Configuration is being changed by another LilacMacro window. Try starting again after it finishes."
                    : "Stop every Macro using this configuration before importing a share.",
                exception);
        }
    }
}
