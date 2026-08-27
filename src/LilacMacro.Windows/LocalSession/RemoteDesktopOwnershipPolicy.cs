using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

internal sealed record RemoteDesktopConfigurationObservation(
    int DenyConnections,
    int ListenerPort,
    string ServiceDll,
    bool OwnedPortInUse);

internal enum RemoteDesktopCleanupDisposition
{
    NotOwned,
    RestoreOwnedConfiguration,
    AlreadyRestored,
    RollbackPartialConfiguration,
}

public static class RemoteDesktopOwnershipPolicy
{
    internal const string ConflictPrefix = "Remote Desktop ownership conflict:";
    internal const int WindowsDefaultPort = 3389;

    internal static IReadOnlyList<string> EvaluateFreshInstall(
        RemoteDesktopConfigurationObservation observation,
        string expectedWindowsServiceDll)
    {
        List<string> conflicts = [];
        if (observation.DenyConnections == 0)
            conflicts.Add("Remote Desktop is already enabled.");
        if (observation.ListenerPort != WindowsDefaultPort)
            conflicts.Add($"The machine RDP listener uses custom port {observation.ListenerPort}.");
        if (!PathsEqual(observation.ServiceDll, expectedWindowsServiceDll))
            conflicts.Add("TermService is owned by a non-Windows service wrapper.");
        if (observation.OwnedPortInUse)
            conflicts.Add($"Port {TermServiceConfigurationManager.LocalPort} is already in use.");

        return conflicts.Count == 0
            ? []
            : [OwnershipConflict(string.Join(' ', conflicts))];
    }

    internal static IReadOnlyList<string> EvaluateManagedConfiguration(IReadOnlyList<string> mismatches) =>
        mismatches.Count == 0
            ? []
            :
            [
                OwnershipConflict(
                    "The machine-wide RDP configuration changed outside LilacMacro. " +
                    string.Join(' ', mismatches)),
            ];

    internal static RemoteDesktopCleanupDisposition EvaluateCleanup(
        bool mutationStarted,
        IReadOnlyList<string> ownedMismatches,
        IReadOnlyList<string> originalMismatches)
    {
        if (!mutationStarted) return RemoteDesktopCleanupDisposition.NotOwned;
        if (ownedMismatches.Count == 0) return RemoteDesktopCleanupDisposition.RestoreOwnedConfiguration;
        if (originalMismatches.Count == 0) return RemoteDesktopCleanupDisposition.AlreadyRestored;
        throw new InvalidOperationException(OwnershipConflict(
            "The current RDP configuration is neither LilacMacro's owned state nor its recorded original state. " +
            "Repair, update, and removal cannot continue safely."));
    }

    public static bool IsOwnershipConflict(IEnumerable<string> problems) =>
        problems.Any(problem => problem.StartsWith(ConflictPrefix, StringComparison.Ordinal));

    private static string OwnershipConflict(string detail) =>
        $"{ConflictPrefix} {detail} LilacMacro will not overwrite it. " +
        "You can run LilacMacro normally inside an existing RDP session. " +
        "Do not use Local instances SET UP while another RDP tool or custom listener is installed.";

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)),
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

internal sealed class RemoteDesktopOwnershipInspector(LocalSessionPaths paths)
{
    private const string TerminalServerKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string ListenerKey = TerminalServerKey + @"\WinStations\RDP-Tcp";
    private const string ServiceParametersKey = @"SYSTEM\CurrentControlSet\Services\TermService\Parameters";

    internal IReadOnlyList<string> FindFreshInstallConflicts()
    {
        using RegistryKey? terminalServer = Registry.LocalMachine.OpenSubKey(TerminalServerKey, writable: false);
        using RegistryKey? listener = Registry.LocalMachine.OpenSubKey(ListenerKey, writable: false);
        using RegistryKey? parameters = Registry.LocalMachine.OpenSubKey(ServiceParametersKey, writable: false);
        RemoteDesktopConfigurationObservation observation = new(
            ReadDword(terminalServer, "fDenyTSConnections", 1),
            ReadDword(listener, "PortNumber", RemoteDesktopOwnershipPolicy.WindowsDefaultPort),
            Convert.ToString(
                parameters?.GetValue("ServiceDll", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames),
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            IsOwnedPortInUse());
        return RemoteDesktopOwnershipPolicy.EvaluateFreshInstall(
            observation,
            Path.Combine(Environment.SystemDirectory, "termsrv.dll"));
    }

    internal IReadOnlyList<string> FindManagedConflicts() =>
        RemoteDesktopOwnershipPolicy.EvaluateManagedConfiguration(
            RegistryStateJournal.FindApplyMismatches(new TermServiceConfigurationManager(paths).GetMutations()));

    private static int ReadDword(RegistryKey? key, string name, int fallback) =>
        Convert.ToInt32(key?.GetValue(name, fallback), System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsOwnedPortInUse()
    {
        try
        {
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            return properties.GetActiveTcpListeners().Any(endpoint => endpoint.Port == TermServiceConfigurationManager.LocalPort)
                || properties.GetActiveUdpListeners().Any(endpoint => endpoint.Port == TermServiceConfigurationManager.LocalPort);
        }
        catch (NetworkInformationException)
        {
            return true;
        }
    }
}
