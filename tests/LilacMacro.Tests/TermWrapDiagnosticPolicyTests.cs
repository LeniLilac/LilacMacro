using LilacMacro.Windows.LocalSession;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Tests;

public sealed class TermWrapDiagnosticPolicyTests
{
    [Fact]
    public void Native_probe_uses_the_export_published_by_termwrap_v06()
    {
        Assert.Equal("ServiceMain", TermWrapNativePreflight.ProbeExportName);
        Assert.Equal("termwrap-self-scan-v3", TermWrapCompatibilityCachePolicy.CurrentProbeVersion);
    }

    [Fact]
    public void No_patch_diagnostics_passes_required_scanner_policy()
    {
        TermWrapDiagnosticAssessment result = TermWrapDiagnosticPolicy.Assess([]);

        Assert.True(result.RequiredPatchesPassed);
        Assert.Empty(result.Advisories);
    }

    [Theory]
    [InlineData("LocalOnlyPatch not found")]
    [InlineData("DefPolicyPatch not found")]
    [InlineData("DefPolicyPatch x64 Unknown functions")]
    [InlineData("SingleUserPatch not found")]
    [InlineData("CDefPolicy_Query not found")]
    [InlineData("CSLQuery_Initialize not found")]
    [InlineData("bInitialized not found")]
    public void Required_patch_diagnostics_fail_closed(string diagnostic)
    {
        TermWrapDiagnosticAssessment result = TermWrapDiagnosticPolicy.Assess([diagnostic]);

        Assert.False(result.RequiredPatchesPassed);
        Assert.Contains(diagnostic, result.RequiredFailures);
    }

    [Theory]
    [InlineData("PropertyAddr not found")]
    [InlineData("PropertyPatch not found")]
    [InlineData("GetConnectionProperty not found")]
    [InlineData("IS_PNP_DISABLED not found")]
    public void Disabled_redirection_patch_diagnostics_are_advisory(string diagnostic)
    {
        TermWrapDiagnosticAssessment result = TermWrapDiagnosticPolicy.Assess([diagnostic]);

        Assert.True(result.RequiredPatchesPassed);
        Assert.Contains(diagnostic, result.Advisories);
    }

    [Fact]
    public void Unrelated_debugger_output_does_not_create_a_false_failure()
    {
        TermWrapDiagnosticAssessment result = TermWrapDiagnosticPolicy.Assess(
            ["Loaded Windows system module", "RUNDLL32: export lookup completed"]);

        Assert.True(result.RequiredPatchesPassed);
        Assert.Empty(result.Advisories);
    }

    [Fact]
    public void Cache_is_reused_only_for_the_exact_probe_and_binary_identity()
    {
        LocalSessionCompatibilityEvidence evidence = ValidEvidence();

        Assert.True(TermWrapCompatibilityCachePolicy.IsReusable(
            evidence, "10.0.1", "X64", new string('A', 64), new string('B', 64)));
        Assert.False(TermWrapCompatibilityCachePolicy.IsReusable(
            evidence, "10.0.2", "X64", new string('A', 64), new string('B', 64)));
        Assert.False(TermWrapCompatibilityCachePolicy.IsReusable(
            evidence, "10.0.1", "X64", new string('C', 64), new string('B', 64)));
        Assert.False(TermWrapCompatibilityCachePolicy.IsReusable(
            evidence, "10.0.1", "X64", new string('A', 64), new string('C', 64)));
        Assert.False(TermWrapCompatibilityCachePolicy.IsReusable(
            evidence with { ProbeVersion = "future-probe" },
            "10.0.1", "X64", new string('A', 64), new string('B', 64)));
    }

    [Fact]
    public void Failed_evidence_is_never_reused()
    {
        LocalSessionCompatibilityEvidence evidence = ValidEvidence() with
        {
            RequiredPatchesPassed = false,
            RequiredPatchDiagnostics = ["SingleUserPatch not found"],
        };

        Assert.False(TermWrapCompatibilityCachePolicy.IsReusable(
            evidence, "10.0.1", "X64", new string('A', 64), new string('B', 64)));
    }

    private static LocalSessionCompatibilityEvidence ValidEvidence() => new()
    {
        OsBuild = "10.0.1",
        Architecture = "X64",
        TermServiceSha256 = new string('A', 64),
        TermWrapSha256 = new string('B', 64),
        RequiredPatchesPassed = true,
    };
}
