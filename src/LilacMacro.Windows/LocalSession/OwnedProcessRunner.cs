using System.ComponentModel;
using System.Diagnostics;

namespace LilacMacro.Windows.LocalSession;

internal static class OwnedProcessRunner
{
    public static async Task RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new(fileName) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new Win32Exception(process.ExitCode, $"{Path.GetFileName(fileName)} failed.");
    }
}
