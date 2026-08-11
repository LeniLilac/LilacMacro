using System.ComponentModel;
using System.Runtime.InteropServices;
using LilacMacro.Core.LocalSession;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

public sealed class TermServiceConfigurationManager(LocalSessionPaths paths)
{
    public const int LocalPort = 33991;
    internal static IReadOnlyList<string> RestartStopOrder { get; } = ["UmRdpService", "TermService"];
    private const string TerminalServerKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string ListenerKey = TerminalServerKey + @"\WinStations\RDP-Tcp";
    private const string ServiceParametersKey = @"SYSTEM\CurrentControlSet\Services\TermService\Parameters";

    public IReadOnlyList<RegistryMutation> GetMutations() =>
    [
        Dword(TerminalServerKey, "fDenyTSConnections", 0),
        Dword(ListenerKey, "PortNumber", LocalPort),
        Dword(ListenerKey, "UserAuthentication", 1),
        Dword(ListenerKey, "SecurityLayer", 2),
        Dword(ListenerKey, "fDisableClip", 1),
        Dword(ListenerKey, "fDisableCdm", 1),
        Dword(ListenerKey, "fDisableCcm", 1),
        Dword(ListenerKey, "fDisableCpm", 1),
        Dword(ListenerKey, "fDisableLPT", 1),
        Dword(ListenerKey, "fDisablePNPRedir", 1),
        Dword(ListenerKey, "fDisableAudioCapture", 1),
        new(ServiceParametersKey, "ServiceDll", RegistryValueKind.ExpandString, paths.TermWrapPath),
    ];

    public void Apply() => RegistryStateJournal.Apply(GetMutations());

    public void Restart()
    {
        using ServiceHandle scm = OpenSCManager(null, null, 0x0001);
        if (scm.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Service Control Manager could not be opened.");
        List<string> stoppedDependents = [];
        foreach (string serviceName in RestartStopOrder)
        {
            bool required = string.Equals(serviceName, "TermService", StringComparison.Ordinal);
            if (StopService(scm, serviceName, required) && !required) stoppedDependents.Add(serviceName);
        }

        StartNamedService(scm, "TermService", required: true);
        foreach (string serviceName in stoppedDependents.AsEnumerable().Reverse())
            StartNamedService(scm, serviceName, required: false);
    }

    private static bool StopService(ServiceHandle scm, string serviceName, bool required)
    {
        using ServiceHandle service = OpenService(scm, serviceName, 0x0010 | 0x0020 | 0x0004);
        if (service.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (!required && error == 1060) return false;
            throw new Win32Exception(error, $"{serviceName} could not be opened.");
        }

        ServiceStatus status = default;
        if (!QueryServiceStatus(service, ref status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{serviceName} state could not be queried.");
        if (status.CurrentState == 1) return false;
        if (status.CurrentState != 3 && !ControlService(service, 1, ref status))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 1062) throw new Win32Exception(error, $"{serviceName} could not be stopped.");
        }
        WaitForState(service, serviceName, 1, TimeSpan.FromSeconds(20));
        return true;
    }

    private static void StartNamedService(ServiceHandle scm, string serviceName, bool required)
    {
        using ServiceHandle service = OpenService(scm, serviceName, 0x0010 | 0x0004);
        if (service.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (!required && error == 1060) return;
            throw new Win32Exception(error, $"{serviceName} could not be opened.");
        }
        if (!StartService(service, 0, null) && Marshal.GetLastWin32Error() != 1056)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{serviceName} could not be started.");
        WaitForState(service, serviceName, 4, TimeSpan.FromSeconds(20));
    }

    private static void WaitForState(ServiceHandle service, string serviceName, uint desiredState, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        ServiceStatus status = default;
        while (DateTime.UtcNow < deadline)
        {
            if (!QueryServiceStatus(service, ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status.CurrentState == desiredState) return;
            Thread.Sleep(200);
        }
        throw new TimeoutException($"{serviceName} did not reach state {desiredState}.");
    }

    private static RegistryMutation Dword(string key, string name, int value) => new(key, name, RegistryValueKind.DWord, value);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }

    private sealed class ServiceHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machineName, string? databaseName, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle scm, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(ServiceHandle service, uint control, ref ServiceStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(ServiceHandle service, int count, string[]? arguments);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(ServiceHandle service, ref ServiceStatus status);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(nint service);
}
