using System.Security.Cryptography;
using System.Text.Json;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.LocalSession;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.Tests;

public sealed class LocalSessionContractTests
{
    [Fact]
    public void Execution_target_defaults_to_local_desktop()
    {
        Assert.Equal(ExecutionTarget.LocalDesktop, new MacroSettings().ExecutionTarget);
    }

    [Theory]
    [InlineData("install")]
    [InlineData("repair")]
    [InlineData("remove")]
    [InlineData("uninstall-cleanup")]
    public void Setup_helper_accepts_only_owned_verbs(string verb)
    {
        Assert.True(LocalSessionSetupVerbPolicy.IsAllowed(verb));
    }

    [Theory]
    [InlineData("")]
    [InlineData("INSTALL")]
    [InlineData("status")]
    [InlineData("remove --force")]
    public void Setup_helper_rejects_other_input(string verb)
    {
        Assert.False(LocalSessionSetupVerbPolicy.IsAllowed(verb));
    }

    [Fact]
    public void Snapshot_requires_matching_identity_version_and_content()
    {
        RunnerRuntimeSnapshot snapshot = ValidSnapshot() with { AppVersion = "1.0.29" };

        LocalSessionValidationResult result = LocalSessionValidation.Validate(
            snapshot,
            "S-1-5-21-100",
            "1.0.30");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Snapshot_accepts_exact_owner_version_and_revision()
    {
        LocalSessionValidationResult result = LocalSessionValidation.Validate(
            ValidSnapshot(),
            "S-1-5-21-100",
            "1.0.30");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Private_server_link_is_ephemeral_and_never_persisted_in_snapshot()
    {
        const string secret = "https://www.roblox.com/share?code=private-test-secret";
        string snapshotJson = JsonSerializer.Serialize(ValidSnapshot());
        string requestJson = JsonSerializer.Serialize(new SessionStartRequest { PrivateServerLink = secret });

        Assert.DoesNotContain(secret, snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("privateServerLink", snapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(secret, requestJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Ready_status_requires_every_health_gate()
    {
        LocalSessionStatus ready = new()
        {
            State = LocalSessionState.Ready,
            CompatibilityPassed = true,
            LoopbackIsolationPassed = true,
            FreshCapturePassed = true,
            RuntimeHostPassed = true,
        };

        Assert.True(ready.CanRun);
        Assert.False((ready with { FreshCapturePassed = false }).CanRun);
        Assert.False((ready with { RuntimeHostPassed = false }).CanRun);
        Assert.False((ready with { Problems = ["stale"] }).CanRun);
    }

    [Fact]
    public void State_machine_rejects_skipping_installation()
    {
        Assert.False(LocalSessionValidation.IsTransitionAllowed(
            LocalSessionState.Absent,
            LocalSessionState.Ready));
        Assert.True(LocalSessionValidation.IsTransitionAllowed(
            LocalSessionState.Absent,
            LocalSessionState.Installing));
        Assert.True(LocalSessionValidation.IsTransitionAllowed(
            LocalSessionState.Degraded,
            LocalSessionState.Ready));
    }

    [Fact]
    public void Provisioning_manifest_rejects_failed_native_evidence()
    {
        LocalSessionProvisioningManifest manifest = new()
        {
            OwnerSid = "S-1-5-21-100",
            CompatibilityEvidence = new LocalSessionCompatibilityEvidence
            {
                OsBuild = "10.0.99999.1",
                Architecture = "X64",
                TermServiceSha256 = new string('A', 64),
                TermWrapSha256 = new string('B', 64),
                RequiredPatchesPassed = false,
                RequiredPatchDiagnostics = ["SingleUserPatch not found"],
            },
        };

        LocalSessionValidationResult result = LocalSessionValidation.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("native patch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Native_payload_requires_the_termwrap_decoder_dependency()
    {
        NativePayloadVerification result = await VerifyNativePayloadAsync(includeDecoder: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("x64/Zydis.dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Native_payload_accepts_the_complete_required_file_set()
    {
        NativePayloadVerification result = await VerifyNativePayloadAsync(includeDecoder: true);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    private static async Task<NativePayloadVerification> VerifyNativePayloadAsync(bool includeDecoder)
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-native-payload-{Guid.NewGuid():N}");
        string nativeRoot = Path.Combine(root, "native");
        string x64Root = Path.Combine(nativeRoot, "x64");
        try
        {
            Directory.CreateDirectory(x64Root);
            List<NativePayloadFile> files = [];
            files.Add(await WriteNativeFileAsync(x64Root, "TermWrap.dll", [1, 2, 3]));
            if (includeDecoder)
                files.Add(await WriteNativeFileAsync(x64Root, "Zydis.dll", [4, 5, 6]));
            await AtomicJsonFile.WriteAsync(
                Path.Combine(nativeRoot, "payload.json"),
                new NativePayloadManifest
                {
                    SchemaVersion = 1,
                    Name = "TermWrap",
                    Version = "0.6",
                    SourceCommit = "test",
                    Files = files,
                });

            LocalSessionPaths paths = new(root, root, nativeRoot);
            return await new NativePayloadVerifier(paths).VerifyAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<NativePayloadFile> WriteNativeFileAsync(
        string directory,
        string name,
        byte[] content)
    {
        string path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, content);
        return new(
            $"x64/{name}",
            content.LongLength,
            Convert.ToHexString(SHA256.HashData(content)));
    }

    private static RunnerRuntimeSnapshot ValidSnapshot() => new()
    {
        Revision = 42,
        AppVersion = "1.0.30",
        OwnerSid = "S-1-5-21-100",
        PlanName = "Test",
        Tasks =
        [
            new RunnerTaskSnapshot
            {
                Id = "task-001",
                Mode = RunnerTaskMode.Story,
                Route = "School Grounds · Act 1",
            },
        ],
        PlacementSetups = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
        StateContexts = [new RunnerStateContextSnapshot("LOBBY", new PixelRect(0, 0, 100, 100), [])],
    };
}
