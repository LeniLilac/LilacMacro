namespace LilacMacro.App.Workspace;

internal sealed class CrossProcessRobloxInputGate(string lockFilePath)
{
    private const string OwnershipError =
        "Another LilacMacro application currently owns Roblox input. Stop its active operation and try again.";

    public static CrossProcessRobloxInputGate CreateDefault()
    {
        string runtimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "runtime");
        return new CrossProcessRobloxInputGate(Path.Combine(runtimeDirectory, "roblox-input.lock"));
    }

    public IDisposable Acquire()
    {
        string? directory = Path.GetDirectoryName(lockFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The Roblox input ownership path is invalid.");
        }
        Directory.CreateDirectory(directory);
        try
        {
            return new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException(OwnershipError, error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new InvalidOperationException(OwnershipError, error);
        }
    }
}
