using System.ComponentModel;
using System.Runtime.InteropServices;
using LilacMacro.Core.LocalSession;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

public sealed class TermServiceConfigurationManager(LocalSessionPaths paths)
{
    public const int LocalPort = 33991;
    internal static IReadOnlyList<string> RestartStopOrder { get; } = ["UmRdpService", "TermService"];
    internal static readonly TimeSpan ServiceTransitionTimeout = TimeSpan.FromSeconds(60);
    internal const int MaximumStopRequests = 3;
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
        int stopRequests = 0;
        if (status.CurrentState != 3)
        {
            if (ControlService(service, 1, ref status))
            {
                stopRequests++;
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1062) throw new Win32Exception(error, $"{serviceName} could not be stopped.");
            }
        }
        WaitForStopped(scm, service, serviceName, ServiceTransitionTimeout, stopRequests);
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
        WaitForState(service, serviceName, 4, ServiceTransitionTimeout);
    }

    private static void WaitForStopped(ServiceHandle scm, ServiceHandle service, string serviceName, TimeSpan timeout, int stopRequests)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        int dependentRestops = 0;
        ServiceStatus status = default;
        while (true)
        {
            if (!QueryServiceStatus(service, ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status.CurrentState == 1) return;
            if (ShouldRetryStop(status.CurrentState, status.ControlsAccepted, stopRequests))
            {
                if (ControlService(service, 1, ref status))
                {
                    stopRequests++;
                    continue;
                }

                int error = Marshal.GetLastWin32Error();
                if (error == 1062) return;
                if (ShouldRestopKnownDependent(serviceName, error, dependentRestops))
                {
                    StopService(scm, "UmRdpService", required: false);
                    dependentRestops++;
                    continue;
                }
                if (error is not (1052 or 1061))
                    throw new Win32Exception(error, $"{serviceName} could not be stopped after it restarted (Win32 error {error}).");
            }

            long remainingMilliseconds = deadline - Environment.TickCount64;
            if (remainingMilliseconds <= 0) break;
            Thread.Sleep(CalculatePollDelay(status.WaitHint, remainingMilliseconds));
        }
        throw new TimeoutException(
            $"{serviceName} did not reach Stopped within {timeout.TotalSeconds:0} seconds after {stopRequests} accepted stop requests " +
            $"(current {StateName(status.CurrentState)}, checkpoint {status.CheckPoint}, wait hint {status.WaitHint} ms, Win32 exit {status.Win32ExitCode}).");
    }

    private static void WaitForState(ServiceHandle service, string serviceName, uint desiredState, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        ServiceStatus status = default;
        while (true)
        {
            if (!QueryServiceStatus(service, ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status.CurrentState == desiredState) return;
            long remainingMilliseconds = deadline - Environment.TickCount64;
            if (remainingMilliseconds <= 0) break;
            Thread.Sleep(CalculatePollDelay(status.WaitHint, remainingMilliseconds));
        }
        throw new TimeoutException(
            $"{serviceName} did not reach {StateName(desiredState)} within {timeout.TotalSeconds:0} seconds " +
            $"(current {StateName(status.CurrentState)}, checkpoint {status.CheckPoint}, wait hint {status.WaitHint} ms, Win32 exit {status.Win32ExitCode}).");
    }

    internal static TimeSpan CalculatePollDelay(uint waitHintMilliseconds, long remainingMilliseconds)
    {
        long suggested = waitHintMilliseconds == 0 ? 500 : waitHintMilliseconds / 10L;
        long bounded = Math.Clamp(suggested, 500, 2_000);
        return TimeSpan.FromMilliseconds(Math.Min(bounded, Math.Max(1, remainingMilliseconds)));
    }

    internal static bool ShouldRetryStop(uint currentState, uint controlsAccepted, int stopRequests) =>
        currentState == 4 && (controlsAccepted & 0x00000001) != 0 && stopRequests < MaximumStopRequests;

    internal static bool ShouldRestopKnownDependent(string serviceName, int error, int dependentRestops) =>
        string.Equals(serviceName, "TermService", StringComparison.Ordinal) &&
        error == 1051 &&
        dependentRestops < MaximumStopRequests;

    internal static string StateName(uint state) => state switch
    {
        1 => "Stopped",
        2 => "Start Pending",
        3 => "Stop Pending",
        4 => "Running",
        5 => "Continue Pending",
        6 => "Pause Pending",
        7 => "Paused",
        _ => $"Unknown ({state})",
    };

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
