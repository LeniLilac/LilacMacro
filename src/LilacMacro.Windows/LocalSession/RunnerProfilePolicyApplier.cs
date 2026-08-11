using System.Diagnostics;
using System.Security.Principal;
using LilacMacro.Core.LocalSession;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

public sealed class RunnerProfilePolicyApplier
{
    public async Task<RunnerProfileReceipt> ApplyAsync(RunnerProfilePolicy policy, CancellationToken cancellationToken)
    {
        LocalSessionValidationResult validation = LocalSessionValidation.Validate(policy);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Runner SID is unavailable.");
        List<string> applied = [];
        foreach (RunnerRegistryRule rule in policy.RegistryRules)
        {
            try
            {
                ApplyRegistryRule(rule, applied);
            }
            catch (UnauthorizedAccessException error)
            {
                throw CreateRegistryAccessException(rule, error);
            }
        }

        List<string> removed = [];
        foreach (RunnerPackageRule rule in policy.PackageRules.Where(item => item.RemoveWhenPresent))
        {
            bool didRemove = await RemovePackageAsync(rule.PackageFamilyName, cancellationToken).ConfigureAwait(false);
            if (didRemove) removed.Add(rule.PackageFamilyName);
        }
        return new RunnerProfileReceipt
        {
            PolicyVersion = policy.Version,
            RunnerSid = sid,
            RemovedPackages = removed,
            AppliedRegistryRules = applied,
        };
    }

    private static void ApplyRegistryRule(RunnerRegistryRule rule, ICollection<string> applied)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(rule.RelativeKey, writable: true)
            ?? throw new InvalidOperationException($"Runner registry policy could not create {rule.RelativeKey}.");
        if (rule.DeleteWhenPresent)
        {
            key.DeleteValue(rule.ValueName, throwOnMissingValue: false);
            if (key.GetValue(rule.ValueName, null) is not null)
                throw new InvalidOperationException($"Runner registry policy could not remove {rule.RelativeKey}|{rule.ValueName}.");
            applied.Add($"{rule.RelativeKey}|{rule.ValueName}|deleted");
            return;
        }

        RegistryValueKind kind = Enum.Parse<RegistryValueKind>(rule.ValueKind, ignoreCase: true);
        object value = kind == RegistryValueKind.DWord
            ? int.Parse(rule.EncodedValue, System.Globalization.CultureInfo.InvariantCulture)
            : rule.EncodedValue;
        key.SetValue(rule.ValueName, value, kind);
        object? observed = key.GetValue(rule.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (!Equals(observed, value) || key.GetValueKind(rule.ValueName) != kind)
            throw new InvalidOperationException($"Runner registry policy could not verify {rule.RelativeKey}|{rule.ValueName}.");
        applied.Add($"{rule.RelativeKey}|{rule.ValueName}");
    }

    internal static UnauthorizedAccessException CreateRegistryAccessException(
        RunnerRegistryRule rule,
        UnauthorizedAccessException error) => new(
            $"Runner registry policy access was denied at {rule.RelativeKey}|{rule.ValueName}: {error.Message}",
            error);

    private static async Task<bool> RemovePackageAsync(string packageName, CancellationToken cancellationToken)
    {
        string escaped = packageName.Replace("'", "''", StringComparison.Ordinal);
        string script = "$p=Get-AppxPackage -Name '" + escaped + "' -ErrorAction SilentlyContinue; if(-not $p){exit 0}; $p|Remove-AppxPackage -ErrorAction Stop; if(Get-AppxPackage -Name '" + escaped + "' -ErrorAction SilentlyContinue){exit 11}; exit 10";
        ProcessStartInfo startInfo = new("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Runner package policy could not start PowerShell.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode is not 0 and not 10) throw new InvalidOperationException($"Runner package removal could not be verified for {packageName}.");
        return process.ExitCode == 10;
    }
}
