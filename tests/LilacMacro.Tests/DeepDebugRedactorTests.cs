using System.Text.Json;
using LilacMacro.App.Diagnostics;

namespace LilacMacro.Tests;

public sealed class DeepDebugRedactorTests
{
    [Fact]
    public void NumericWindowsUserDoesNotCorruptStructuredDiagnostics()
    {
        const string json = """
            {"appVersion":"1.0.174.0","artifacts":19504,"path":"C:\\Users\\1\\capture.png"}
            """;

        string redacted = DeepDebugRedactor.Redact(json, "1");

        using JsonDocument document = JsonDocument.Parse(redacted);
        Assert.Equal("1.0.174.0", document.RootElement.GetProperty("appVersion").GetString());
        Assert.Equal(19504, document.RootElement.GetProperty("artifacts").GetInt32());
        Assert.Equal(
            "C:\\Users\\[REDACTED WINDOWS USER]\\capture.png",
            document.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void OrdinaryWindowsUserIsRedactedOutsideProfilePaths()
    {
        string redacted = DeepDebugRedactor.Redact("owner=micha", "micha");

        Assert.Equal("owner=[REDACTED WINDOWS USER]", redacted);
    }
}
