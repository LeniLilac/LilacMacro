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

public sealed class FirewallIsolationManager
{
    public const string TcpRule = "LilacMacro Local Runner RDP Block TCP";
    public const string UdpRule = "LilacMacro Local Runner RDP Block UDP";

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

    public async Task<bool> VerifyLoopbackOnlyAsync(CancellationToken cancellationToken)
    {
        if (!await CanConnectAsync(IPAddress.Loopback, cancellationToken).ConfigureAwait(false)) return false;
        return IsolationRulesAreExact();
    }

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
        string.Equals(rule.RemoteAddresses, "*", StringComparison.Ordinal);

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

    private static bool IsolationRulesAreExact()
    {
        Type? type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        if (type is null) return false;
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
                return false;
            }
        }
        return tcp && udp;
    }

    private static Task AddRuleAsync(string name, string protocol, CancellationToken cancellationToken) =>
        OwnedProcessRunner.RunAsync("netsh.exe", ["advfirewall", "firewall", "add", "rule", $"name={name}", "dir=in", "action=block", $"protocol={protocol}", $"localport={TermServiceConfigurationManager.LocalPort}", "profile=any", "enable=yes"], cancellationToken);

    private static async Task DeleteRuleAsync(string name, CancellationToken cancellationToken)
    {
        try { await OwnedProcessRunner.RunAsync("netsh.exe", ["advfirewall", "firewall", "delete", "rule", $"name={name}"], cancellationToken).ConfigureAwait(false); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task<bool> CanConnectAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using TcpClient client = new(address.AddressFamily);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try { await client.ConnectAsync(address, TermServiceConfigurationManager.LocalPort, timeout.Token).ConfigureAwait(false); return true; }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException) { return false; }
    }
}
