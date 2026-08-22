using System.Globalization;
using Microsoft.Win32;

namespace LilacMacro.Windows.SystemInformation;

public static class WindowsVersionDescription
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static string Read()
    {
        string fallback = Environment.OSVersion.VersionString;
        if (!OperatingSystem.IsWindows()) return fallback;

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
            return Format(fallback, Environment.OSVersion.Version, key?.GetValue("UBR"));
        }
        catch (Exception error) when (error is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException
            or PlatformNotSupportedException)
        {
            return fallback;
        }
    }

    internal static string Format(string fallback, Version version, object? updateBuildRevision)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(version);
        if (version.Build < 0 || !TryReadRevision(updateBuildRevision, out int revision))
            return fallback;

        string baseVersion = version.ToString();
        string fullVersion = string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{version.Build}.{revision}");
        return fallback.EndsWith(baseVersion, StringComparison.Ordinal)
            ? fallback[..^baseVersion.Length] + fullVersion
            : fullVersion;
    }

    private static bool TryReadRevision(object? value, out int revision)
    {
        switch (value)
        {
            case int number when number >= 0:
                revision = number;
                return true;
            case long number when number is >= 0 and <= int.MaxValue:
                revision = (int)number;
                return true;
            case string text when int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int number) && number >= 0:
                revision = number;
                return true;
            default:
                revision = 0;
                return false;
        }
    }
}
