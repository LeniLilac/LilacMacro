using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.SessionSetup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!LocalSessionSetupVerbPolicy.AreArgumentsAllowed(args)) return 64;
        string root = AppContext.BaseDirectory;
        LocalSessionProvisioner provisioner = new(LocalSessionPaths.CreateDefault(root));
        try
        {
            CancellationToken cancellationToken = CancellationToken.None;
            if (args[0] == "install") provisioner.InstallOrRepairAsync(BuildVersion(), repair: false, cancellationToken).GetAwaiter().GetResult();
            else if (args[0] == "repair") provisioner.InstallOrRepairAsync(BuildVersion(), repair: true, cancellationToken).GetAwaiter().GetResult();
            else if (args[0] == "add-shared") new LocalInstanceProfileManager(LocalSessionPaths.CreateDefault(root)).AddAsync(BuildVersion(), RunnerConfigurationMode.Shared, cancellationToken).GetAwaiter().GetResult();
            else if (args[0] == "add-isolated") new LocalInstanceProfileManager(LocalSessionPaths.CreateDefault(root)).AddAsync(BuildVersion(), RunnerConfigurationMode.Isolated, cancellationToken).GetAwaiter().GetResult();
            else if (args[0] == "remove-profile") new LocalInstanceProfileManager(LocalSessionPaths.CreateDefault(root)).RemoveAsync(args[1], cancellationToken).GetAwaiter().GetResult();
            else if (args[0] == "relaunch-update") new CoordinatedUpdateRelauncher(LocalSessionPaths.CreateDefault(root)).RelaunchAsync(args[1], cancellationToken).GetAwaiter().GetResult();
            else if (args[0] == "relaunch-runners") new CoordinatedUpdateRelauncher(LocalSessionPaths.CreateDefault(root)).RelaunchConfiguredAsync(cancellationToken).GetAwaiter().GetResult();
            else if (LocalSessionSetupVerbPolicy.IsRemoval(args[0]))
                provisioner.RemoveAsync(args[0] == "uninstall-cleanup", cancellationToken).GetAwaiter().GetResult();
            else return 64;
            return 0;
        }
        catch (Exception exception)
        {
            try { provisioner.RecordUnhandledFailureAsync(args[0], exception).GetAwaiter().GetResult(); }
            catch (Exception recordingError) when (recordingError is IOException or UnauthorizedAccessException) { }
            return 1;
        }
    }

    private static string BuildVersion() => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
