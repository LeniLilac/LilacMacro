using System.Text;
using System.Security;
using LilacMacro.Core.Services;
using Microsoft.Win32;
using Vortice.DXGI;

namespace LilacMacro.Windows;

public static class WindowsTelemetryDeviceContextProvider
{
    private const int MaximumModelLength = 96;
    private const string DisplayAdaptersRegistryPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static ProductTelemetryDeviceContext Read()
    {
        (int width, int height) = WindowsDesktopMetrics.PrimaryDisplaySize();
        return new ProductTelemetryDeviceContext(
            ReadProcessorModel(),
            ReadGraphicsModel(),
            width,
            height);
    }

    private static string ReadProcessorModel()
    {
        try
        {
            using RegistryKey? processor = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                writable: false);
            return NormalizeModelForTelemetry(processor?.GetValue("ProcessorNameString") as string);
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException
            or IOException)
        {
            return "unknown";
        }
    }

    private static string ReadGraphicsModel()
    {
        List<(string? Model, ulong DedicatedMemory)> candidates = [];
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint index = 0; ; index++)
            {
                if (factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Failure) break;
                using (adapter)
                {
                    AdapterDescription1 description = adapter.Description1;
                    candidates.Add((
                        description.Description,
                        Convert.ToUInt64(description.DedicatedVideoMemory)));
                }
            }
        }
        catch
        {
            // RDP and isolated local sessions can hide the physical adapter from DXGI.
        }
        AddRegisteredGraphicsModels(candidates);
        return SelectBestGraphicsModel(candidates);
    }

    private static void AddRegisteredGraphicsModels(List<(string? Model, ulong DedicatedMemory)> candidates)
    {
        try
        {
            using RegistryKey? adapters = Registry.LocalMachine.OpenSubKey(
                DisplayAdaptersRegistryPath,
                writable: false);
            if (adapters is null) return;
            foreach (string name in adapters.GetSubKeyNames())
            {
                using RegistryKey? adapter = adapters.OpenSubKey(name, writable: false);
                string? model = adapter?.GetValue("DriverDesc") as string ??
                    adapter?.GetValue("HardwareInformation.AdapterString") as string;
                candidates.Add((model, 0));
            }
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Unknown is safer than reporting an untrusted or partial model string.
        }
    }

    internal static string SelectBestGraphicsModel(
        IEnumerable<(string? Model, ulong DedicatedMemory)> candidates)
    {
        (string Model, int Rank, ulong Memory) best = ("unknown", 0, 0);
        foreach ((string? source, ulong memory) in candidates)
        {
            string model = NormalizeModelForTelemetry(source);
            int rank = GraphicsRank(model);
            if (rank > best.Rank || rank == best.Rank && memory > best.Memory)
                best = (model, rank, memory);
        }
        return best.Model;
    }

    private static int GraphicsRank(string model) =>
        model.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase) ? 3
        : model.StartsWith("AMD ", StringComparison.OrdinalIgnoreCase) ? 2
        : model.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase) ? 1
        : 0;

    internal static string NormalizeModelForTelemetry(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "unknown";
        string value = source
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        string? vendor = VendorPrefix(value);
        if (vendor is null) return "unknown";
        value = vendor + value[vendor.Length..].TrimStart();

        StringBuilder normalized = new(Math.Min(value.Length, MaximumModelLength));
        bool pendingSpace = false;
        foreach (char character in value.Trim())
        {
            if (normalized.Length >= MaximumModelLength) break;
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or '+' or '-' or '(' or ')'))
                continue;
            if (pendingSpace && normalized.Length < MaximumModelLength) normalized.Append(' ');
            pendingSpace = false;
            normalized.Append(character);
        }
        return normalized.Length == 0 ? "unknown" : normalized.ToString();
    }

    private static string? VendorPrefix(string value) =>
        value.StartsWith("AMD ", StringComparison.OrdinalIgnoreCase) ? "AMD "
        : value.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase) ? "Intel "
        : value.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase) ? "NVIDIA "
        : value.StartsWith("Qualcomm ", StringComparison.OrdinalIgnoreCase) ? "Qualcomm "
        : null;
}
