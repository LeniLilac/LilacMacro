using System.Globalization;
using System.Text.Json;
using LilacMacro.Core.LocalSession;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

public sealed record RegistryMutation(string SubKey, string ValueName, RegistryValueKind Kind, object Value);

public static class RegistryStateJournal
{
    public static IReadOnlyList<OriginalSystemValue> Capture(IEnumerable<RegistryMutation> mutations)
    {
        List<OriginalSystemValue> originals = [];
        foreach (RegistryMutation mutation in mutations.DistinctBy(item => $"{item.SubKey}|{item.ValueName}", StringComparer.OrdinalIgnoreCase))
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(mutation.SubKey, writable: false);
            object? value = key?.GetValue(mutation.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            bool existed = value is not null;
            RegistryValueKind? kind = existed ? key!.GetValueKind(mutation.ValueName) : null;
            originals.Add(new OriginalSystemValue(
                "registry-hklm",
                $"{mutation.SubKey}|{mutation.ValueName}",
                existed,
                kind?.ToString(),
                existed ? Encode(value!, kind!.Value) : null));
        }
        return originals;
    }

    public static void Apply(IEnumerable<RegistryMutation> mutations)
    {
        foreach (RegistryMutation mutation in mutations)
        {
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(mutation.SubKey, writable: true)
                ?? throw new InvalidOperationException($"Registry key could not be created: {mutation.SubKey}");
            key.SetValue(mutation.ValueName, mutation.Value, mutation.Kind);
        }
    }

    public static IReadOnlyList<string> FindApplyMismatches(IEnumerable<RegistryMutation> mutations)
    {
        List<string> problems = [];
        foreach (RegistryMutation mutation in mutations.DistinctBy(
                     item => $"{item.SubKey}|{item.ValueName}",
                     StringComparer.OrdinalIgnoreCase))
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(mutation.SubKey, writable: false);
            object? current = key?.GetValue(
                mutation.ValueName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (current is null)
            {
                problems.Add($"Registry value is missing: HKLM\\{mutation.SubKey}\\{mutation.ValueName}");
                continue;
            }

            RegistryValueKind kind = key!.GetValueKind(mutation.ValueName);
            if (kind != mutation.Kind
                || !string.Equals(
                    Encode(current, kind),
                    Encode(mutation.Value, mutation.Kind),
                    StringComparison.Ordinal))
            {
                problems.Add($"Registry value differs from the owned configuration: HKLM\\{mutation.SubKey}\\{mutation.ValueName}");
            }
        }
        return problems;
    }

    public static void Restore(IEnumerable<OriginalSystemValue> originals)
    {
        foreach (OriginalSystemValue original in originals.Where(item => item.Kind == "registry-hklm").Reverse())
        {
            string[] parts = original.Identifier.Split('|', 2);
            if (parts.Length != 2) throw new InvalidDataException("Provisioning journal contains a malformed registry identifier.");
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(parts[0], writable: true)
                ?? throw new InvalidOperationException($"Registry key could not be restored: {parts[0]}");
            if (!original.Existed) key.DeleteValue(parts[1], throwOnMissingValue: false);
            else
            {
                RegistryValueKind kind = Enum.Parse<RegistryValueKind>(original.ValueType!, ignoreCase: false);
                key.SetValue(parts[1], Decode(original.EncodedValue!, kind), kind);
            }
        }
    }

    public static IReadOnlyList<string> FindRestoreMismatches(IEnumerable<OriginalSystemValue> originals)
    {
        List<string> problems = [];
        foreach (OriginalSystemValue original in originals.Where(item => item.Kind == "registry-hklm"))
        {
            string[] parts = original.Identifier.Split('|', 2);
            if (parts.Length != 2) { problems.Add($"Malformed registry journal entry: {original.Identifier}"); continue; }
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(parts[0], writable: false);
            object? current = key?.GetValue(parts[1], null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (!original.Existed)
            {
                if (current is not null) problems.Add($"Registry value remains: HKLM\\{parts[0]}\\{parts[1]}");
                continue;
            }
            if (current is null) { problems.Add($"Registry value was not restored: HKLM\\{parts[0]}\\{parts[1]}"); continue; }
            RegistryValueKind kind = key!.GetValueKind(parts[1]);
            string encoded = Encode(current, kind);
            if (!string.Equals(kind.ToString(), original.ValueType, StringComparison.Ordinal)
                || !string.Equals(encoded, original.EncodedValue, StringComparison.Ordinal))
                problems.Add($"Registry value differs from its original state: HKLM\\{parts[0]}\\{parts[1]}");
        }
        return problems;
    }

    private static string Encode(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value),
        RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static object Decode(string value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary => Convert.FromBase64String(value),
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value) ?? [],
        RegistryValueKind.DWord => int.Parse(value, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(value, CultureInfo.InvariantCulture),
        _ => value,
    };
}
