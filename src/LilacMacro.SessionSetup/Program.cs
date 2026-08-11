using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.SessionSetup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !LocalSessionSetupVerbPolicy.IsAllowed(args[0])) return 64;
        try
        {
            string root = AppContext.BaseDirectory;
            LocalSessionProvisioner provisioner = new(LocalSessionPaths.CreateDefault(root));
            CancellationToken cancellationToken = CancellationToken.None;
            if (args[0] == "install") provisioner.InstallOrRepairAsync(BuildVersion(), repair: false, cancellationToken).GetAwaiter().GetResult();
            else if (args[0] == "repair") provisioner.InstallOrRepairAsync(BuildVersion(), repair: true, cancellationToken).GetAwaiter().GetResult();
            else if (LocalSessionSetupVerbPolicy.IsRemoval(args[0]))
                provisioner.RemoveAsync(args[0] == "uninstall-cleanup", cancellationToken).GetAwaiter().GetResult();
            else return 64;
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static string BuildVersion() => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
