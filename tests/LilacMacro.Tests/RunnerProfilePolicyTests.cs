using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.Tests;

public sealed class RunnerProfilePolicyTests
{
    [Fact]
    public void Default_policy_is_runner_scoped_and_allowlisted()
    {
        RunnerProfilePolicy policy = new();
        LocalSessionValidationResult result = LocalSessionValidation.Validate(policy);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(policy.PackageRules, rule => rule.PackageFamilyName.Contains('*'));
        Assert.DoesNotContain(policy.PackageRules, rule => rule.RemoveWhenPresent);
        Assert.DoesNotContain(policy.RegistryRules, rule =>
            rule.RelativeKey.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(policy.RegistryRules, rule =>
            rule.RelativeKey.Contains("Defender", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wildcard_package_removal_is_rejected()
    {
        RunnerProfilePolicy policy = new()
        {
            PackageRules = [new RunnerPackageRule("Microsoft.*", true)],
        };

        Assert.False(LocalSessionValidation.Validate(policy).IsValid);
    }

    [Fact]
    public void Global_or_defender_registry_paths_are_rejected()
    {
        RunnerProfilePolicy policy = new()
        {
            RegistryRules =
            [
                new RunnerRegistryRule("HKEY_LOCAL_MACHINE\\Software", "Value", "DWord", "1"),
                new RunnerRegistryRule("Software\\Microsoft\\Defender", "Value", "DWord", "1"),
            ],
        };

        LocalSessionValidationResult result = LocalSessionValidation.Validate(policy);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Default_policy_has_only_exact_package_names_and_runner_hive_rules()
    {
        RunnerProfilePolicy policy = new();

        Assert.All(policy.PackageRules, rule => Assert.DoesNotContain("*", rule.PackageFamilyName));
        Assert.Contains(policy.RegistryRules, rule => rule.DeleteWhenPresent && rule.ValueName == "OneDrive");
        Assert.Contains(policy.RegistryRules, rule => rule.RelativeKey.Contains("Notifications", StringComparison.Ordinal));
        Assert.DoesNotContain(policy.RegistryRules, rule => rule.ValueName == "TaskbarDa");
        RunnerRegistryRule[] managedPolicies =
        [.. policy.RegistryRules.Where(rule => rule.RelativeKey.StartsWith(@"Software\Policies", StringComparison.OrdinalIgnoreCase))];
        Assert.Equal(2, managedPolicies.Length);
        Assert.Contains(managedPolicies, rule => rule.RelativeKey == @"Software\Policies\Microsoft\Windows\OOBE"
            && rule.ValueName == "DisablePrivacyExperience" && rule.EncodedValue == "1");
        Assert.Contains(managedPolicies, rule => rule.RelativeKey == @"Software\Policies\Microsoft\Edge"
            && rule.ValueName == "HideFirstRunExperience" && rule.EncodedValue == "1");
        Assert.Contains(policy.RegistryRules, rule => rule.ValueName == "HideIcons" && rule.EncodedValue == "1");
        Assert.Contains(policy.RegistryRules, rule => rule.RelativeKey == @"Control Panel\Colors"
            && rule.ValueName == "Background" && rule.EncodedValue == "0 0 0");
    }

    [Fact]
    public void Profile_failure_requires_bounded_known_diagnostic_fields()
    {
        Assert.True(LocalSessionValidation.Validate(new RunnerProfileFailure
        {
            FailureCode = "profile-policy-io-failed",
            Detail = "The runner policy file could not be read.",
        }).IsValid);
        Assert.False(LocalSessionValidation.Validate(new RunnerProfileFailure
        {
            SchemaVersion = 2,
            FailureCode = "unknown",
            Detail = "line one\nline two",
        }).IsValid);
    }

    [Fact]
    public void Registry_access_failure_identifies_the_exact_rule()
    {
        RunnerRegistryRule rule = new(
            @"Software\Policies\Microsoft\Windows\OneDrive",
            "DisableFileSyncNGSC",
            "DWord",
            "1");

        UnauthorizedAccessException error = RunnerProfilePolicyApplier.CreateRegistryAccessException(
            rule,
            new UnauthorizedAccessException("Attempted to perform an unauthorized operation."));

        Assert.Contains(rule.RelativeKey, error.Message, StringComparison.Ordinal);
        Assert.Contains(rule.ValueName, error.Message, StringComparison.Ordinal);
        Assert.IsType<UnauthorizedAccessException>(error.InnerException);
    }
}
