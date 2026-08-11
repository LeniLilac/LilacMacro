using System.Security.Cryptography;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed record TermWrapDiagnosticAssessment(
    IReadOnlyList<string> RequiredFailures,
    IReadOnlyList<string> Advisories)
{
    public bool RequiredPatchesPassed => RequiredFailures.Count == 0;
}

public static class TermWrapDiagnosticPolicy
{
    private static readonly string[] AdvisoryMarkers =
    [
        "PropertyAddr not found",
        "PropertyPatch not found",
        "GetConnectionProperty not found",
        "IS_PNP_DISABLED not found",
    ];

    public static TermWrapDiagnosticAssessment Assess(IEnumerable<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        List<string> requiredFailures = [];
        List<string> advisories = [];
        foreach (string raw in diagnostics)
        {
            string diagnostic = raw.Trim().TrimEnd('\0');
            if (diagnostic.Length == 0) continue;
            if (AdvisoryMarkers.Any(marker => diagnostic.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                advisories.Add(diagnostic);
                continue;
            }
            if (diagnostic.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || diagnostic.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
                requiredFailures.Add(diagnostic);
        }
        return new(requiredFailures, advisories);
    }
}

public static class TermWrapCompatibilityCachePolicy
{
    public const string CurrentProbeVersion = "termwrap-self-scan-v3";

    public static bool IsReusable(
        LocalSessionCompatibilityEvidence? evidence,
        string osBuild,
        string architecture,
        string termServiceSha256,
        string termWrapSha256) => evidence is not null
        && evidence.SchemaVersion == LocalSessionCompatibilityEvidence.CurrentSchemaVersion
        && evidence.RequiredPatchesPassed
        && evidence.RequiredPatchDiagnostics.Count == 0
        && string.Equals(evidence.ProbeVersion, CurrentProbeVersion, StringComparison.Ordinal)
        && string.Equals(evidence.OsBuild, osBuild, StringComparison.Ordinal)
        && string.Equals(evidence.Architecture, architecture, StringComparison.Ordinal)
        && HashEquals(evidence.TermServiceSha256, termServiceSha256)
        && HashEquals(evidence.TermWrapSha256, termWrapSha256);

    private static bool HashEquals(string expected, string actual)
    {
        if (expected.Length != 64 || actual.Length != 64) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual));
        }
        catch (FormatException) { return false; }
    }
}
