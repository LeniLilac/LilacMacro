using System.ComponentModel;
using System.Diagnostics;

namespace LilacMacro.Windows;

public sealed class RobloxClientLifecycleService
{
    private static readonly string[] SupportedClientNames = ["RobloxPlayerBeta", "Windows10Universal"];
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(5);
    private readonly RobloxGlobalSettingsStore _settings;

    public RobloxClientLifecycleService() : this(new RobloxGlobalSettingsStore()) { }

    internal RobloxClientLifecycleService(RobloxGlobalSettingsStore settings) => _settings = settings;

    public async Task PrepareForPrivateServerLaunchAsync(
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        status?.Invoke("STOPPING ROBLOX FOR SETTINGS NORMALIZATION");
        await StopCurrentSessionClientsAsync(cancellationToken).ConfigureAwait(false);
        status?.Invoke("NORMALIZING ROBLOX CLIENT SETTINGS");
        var result = await _settings.NormalizeAsync(cancellationToken).ConfigureAwait(false);
        status?.Invoke(result.Changed
            ? $"ROBLOX SETTINGS NORMALIZED | {result.ChangedSettings.Count} VALUES"
            : "ROBLOX SETTINGS ALREADY NORMALIZED");
    }

    private static async Task StopCurrentSessionClientsAsync(CancellationToken cancellationToken)
    {
        Process[] clients = FindCurrentSessionClients();
        try
        {
            foreach (Process client in clients)
            {
                try
                {
                    if (!client.HasExited) _ = client.CloseMainWindow();
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception)
                {
                }
            }

            await WaitForExitAsync(clients, GracefulExitTimeout, cancellationToken).ConfigureAwait(false);
            foreach (Process client in clients.Where(client => !HasExited(client)))
            {
                try { client.Kill(entireProcessTree: false); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            }
            await WaitForExitAsync(clients, ForcedExitTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (Process client in clients) client.Dispose();
        }

        Process[] remaining = FindCurrentSessionClients();
        try
        {
            if (remaining.Length > 0)
                throw new InvalidOperationException("Roblox did not close before settings normalization.");
        }
        finally
        {
            foreach (Process client in remaining) client.Dispose();
        }
    }

    private static Process[] FindCurrentSessionClients()
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        List<Process> result = [];
        foreach (string processName in SupportedClientNames)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.SessionId == sessionId) result.Add(process);
                    else process.Dispose();
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception)
                {
                    process.Dispose();
                }
            }
        }
        return [.. result];
    }

    private static async Task WaitForExitAsync(
        IReadOnlyList<Process> clients,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (clients.Any(client => !HasExited(client)))
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal static bool IsSupportedClient(string processName) =>
        SupportedClientNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return true; }
    }
}
