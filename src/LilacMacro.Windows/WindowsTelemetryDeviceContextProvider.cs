using System.Text;
using System.Security;
using LilacMacro.Core.Services;
using Microsoft.Win32;
using Vortice.DXGI;

namespace LilacMacro.Windows;

public static class WindowsTelemetryDeviceContextProvider
{
    private const int MaximumModelLength = 96;

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
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            (string Model, int Rank, ulong Memory) best = ("unknown", 0, 0);
            for (uint index = 0; ; index++)
            {
                if (factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Failure) break;
                using (adapter)
                {
                    AdapterDescription1 description = adapter.Description1;
                    string model = NormalizeModelForTelemetry(description.Description);
                    int rank = GraphicsRank(model);
                    ulong memory = Convert.ToUInt64(description.DedicatedVideoMemory);
                    if (rank > best.Rank || rank == best.Rank && memory > best.Memory)
                        best = (model, rank, memory);
                }
            }
            return best.Model;
        }
        catch
        {
            return "unknown";
        }
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
