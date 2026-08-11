using System.Net;
using System.Net.Sockets;

namespace LilacMacro.Windows.LocalSession;

internal sealed record FirewallRuleObservation(
    string Name,
    bool Enabled,
    int Direction,
    int Action,
    int Protocol,
    string LocalPorts,
    int Profiles,
    string RemoteAddresses);

public sealed record FirewallIsolationVerification(bool Passed, string Problem)
{
    public static FirewallIsolationVerification Success { get; } = new(true, string.Empty);
}

public sealed class FirewallIsolationManager
{
    public const string TcpRule = "LilacMacro Local Runner RDP Block TCP";
    public const string UdpRule = "LilacMacro Local Runner RDP Block UDP";
    public const string AuthorizedLoopbackAddress = "127.0.0.2";
    internal const string ExternalRemoteAddresses =
        "0.0.0.0-127.0.0.0,127.0.0.2-255.255.255.255,::-::,::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";
    internal static readonly TimeSpan ListenerReadyTimeout = TimeSpan.FromSeconds(15);
    private const string ListenerStartupPrefix = "The RDP listener did not accept ";

    public async Task InstallAsync(CancellationToken cancellationToken)
    {
        await RemoveAsync(cancellationToken).ConfigureAwait(false);
        await AddRuleAsync(TcpRule, "TCP", cancellationToken).ConfigureAwait(false);
        await AddRuleAsync(UdpRule, "UDP", cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        await DeleteRuleAsync(TcpRule, cancellationToken).ConfigureAwait(false);
        await DeleteRuleAsync(UdpRule, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirewallIsolationVerification> VerifyLoopbackOnlyAsync(CancellationToken cancellationToken)
    {
        FirewallIsolationVerification rules = InspectIsolationRules();
        if (!rules.Passed) return rules;
        if (!await WaitForLoopbackListenerAsync(cancellationToken).ConfigureAwait(false))
            return new(false, $"{ListenerStartupPrefix}{AuthorizedLoopbackAddress}:{TermServiceConfigurationManager.LocalPort} within {ListenerReadyTimeout.TotalSeconds:0} seconds after restart.");
        return FirewallIsolationVerification.Success;
    }

    internal static bool IsListenerStartupDelay(FirewallIsolationVerification verification) =>
        !verification.Passed
        && verification.Problem.StartsWith(ListenerStartupPrefix, StringComparison.Ordinal);

    internal static bool IsExpectedIsolationRule(
        FirewallRuleObservation rule,
        string expectedName,
        int expectedProtocol) =>
        string.Equals(rule.Name, expectedName, StringComparison.Ordinal) &&
        rule.Enabled &&
        rule.Direction == 1 &&
        rule.Action == 0 &&
        rule.Protocol == expectedProtocol &&
        string.Equals(rule.LocalPorts, TermServiceConfigurationManager.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
        rule.Profiles == int.MaxValue &&
        RemoteAddressScopeIsExpected(rule.RemoteAddresses);

    internal static bool RemoteAddressScopeIsExpected(string actual)
    {
        string[] expected = ExternalRemoteAddresses.Split(',');
        string[] observed = actual.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return observed.Length == expected.Length &&
               observed.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected);
    }

    public bool RulesExist()
    {
        Type? type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (type is null) return false;
        dynamic policy = Activator.CreateInstance(type)!;
        bool tcp = false;
        bool udp = false;
        foreach (dynamic rule in policy.Rules)
        {
            string? name = rule.Name as string;
            tcp |= string.Equals(name, TcpRule, StringComparison.Ordinal);
            udp |= string.Equals(name, UdpRule, StringComparison.Ordinal);
        }
        return tcp || udp;
    }

    private static FirewallIsolationVerification InspectIsolationRules()
    {
        Type? type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (type is null) return new(false, "Windows Firewall policy is unavailable.");
        dynamic policy = Activator.CreateInstance(type)!;
        bool tcp = false;
        bool udp = false;
        foreach (dynamic rule in policy.Rules)
        {
            string? name = rule.Name as string;
            if (!string.Equals(name, TcpRule, StringComparison.Ordinal) &&
                !string.Equals(name, UdpRule, StringComparison.Ordinal)) continue;
            try
            {
                FirewallRuleObservation observation = new(
                    name,
                    Convert.ToBoolean(rule.Enabled, System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToInt32(rule.Direction, System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToInt32(rule.Action, System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToInt32(rule.Protocol, System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToString(rule.LocalPorts, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    Convert.ToInt32(rule.Profiles, System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToString(rule.RemoteAddresses, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                tcp |= IsExpectedIsolationRule(observation, TcpRule, 6);
                udp |= IsExpectedIsolationRule(observation, UdpRule, 17);
            }
            catch
            {
                return new(false, $"Windows Firewall could not inspect the owned rule '{name}'.");
            }
        }
        return tcp && udp
            ? FirewallIsolationVerification.Success
            : new(false, "The owned TCP/UDP firewall rules did not preserve the exact external-address block scope.");
    }

    private static Task AddRuleAsync(string name, string protocol, CancellationToken cancellationToken) =>
        OwnedProcessRunner.RunAsync("netsh.exe", ["advfirewall", "firewall", "add", "rule", $"name={name}", "dir=in", "action=block", $"protocol={protocol}", $"localport={TermServiceConfigurationManager.LocalPort}", $"remoteip={ExternalRemoteAddresses}", "profile=any", "enable=yes"], cancellationToken);

    private static async Task DeleteRuleAsync(string name, CancellationToken cancellationToken)
    {
        try { await OwnedProcessRunner.RunAsync("netsh.exe", ["advfirewall", "firewall", "delete", "rule", $"name={name}"], cancellationToken).ConfigureAwait(false); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task<bool> WaitForLoopbackListenerAsync(CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)ListenerReadyTimeout.TotalMilliseconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long remainingMilliseconds = deadline - Environment.TickCount64;
            if (remainingMilliseconds <= 0) return false;
            TimeSpan attemptTimeout = TimeSpan.FromMilliseconds(Math.Min(2_000, remainingMilliseconds));
            if (await CanConnectAsync(IPAddress.Parse(AuthorizedLoopbackAddress), attemptTimeout, cancellationToken).ConfigureAwait(false)) return true;
            remainingMilliseconds = deadline - Environment.TickCount64;
            if (remainingMilliseconds <= 0) return false;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(250, remainingMilliseconds)), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> CanConnectAsync(IPAddress address, TimeSpan attemptTimeout, CancellationToken cancellationToken)
    {
        using TcpClient client = new(address.AddressFamily);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(attemptTimeout);
        try { await client.ConnectAsync(address, TermServiceConfigurationManager.LocalPort, timeout.Token).ConfigureAwait(false); return true; }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException) { return false; }
    }
}
