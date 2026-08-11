using LilacMacro.Core.LocalSession;

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
    }
}
