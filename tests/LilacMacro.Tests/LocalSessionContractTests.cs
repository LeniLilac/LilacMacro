using System.Security.Cryptography;
using System.Runtime.InteropServices;
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

    [Theory]
    [InlineData(LocalSessionState.Ready, true, true, true)]
    [InlineData(LocalSessionState.Degraded, true, true, true)]
    [InlineData(LocalSessionState.Degraded, false, true, false)]
    [InlineData(LocalSessionState.Degraded, true, false, false)]
    [InlineData(LocalSessionState.Absent, true, true, false)]
    [InlineData(LocalSessionState.RecoveryRequired, true, true, false)]
    public void Interactive_session_requires_provisioned_compatible_loopback_state(
        LocalSessionState state,
        bool compatibilityPassed,
        bool loopbackIsolationPassed,
        bool expected)
    {
        LocalSessionStatus status = new()
        {
            State = state,
            CompatibilityPassed = compatibilityPassed,
            LoopbackIsolationPassed = loopbackIsolationPassed,
            Problems = ["Fresh runner capture has not been verified."],
        };

        Assert.Equal(expected, status.CanOpenInteractiveSession);
    }

    [Fact]
    public void Interactive_session_uses_fullscreen_loopback_rdp_viewport()
    {
        System.Diagnostics.ProcessStartInfo start = LocalSessionDesktopController.CreateRdpStartInfo();

        Assert.Equal("mstsc.exe", start.FileName);
        Assert.Equal("/v:127.0.0.1:33991 /f", start.Arguments);
        Assert.True(start.UseShellExecute);
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

    [Theory]
    [InlineData(false, false, LocalSessionState.Absent)]
    [InlineData(true, false, LocalSessionState.RecoveryRequired)]
    [InlineData(false, true, LocalSessionState.Installing)]
    public void Interrupted_setup_status_is_reconciled_from_journal_and_helper_evidence(
        bool journalExists,
        bool helperActive,
        LocalSessionState expectedState)
    {
        LocalSessionStatus installing = new()
        {
            State = LocalSessionState.Installing,
            StatusCode = "installing",
            Detail = "Installing.",
        };

        LocalSessionStatus result = LocalSessionValidation.ReconcileInterruptedOperation(
            installing,
            journalExists,
            helperActive);

        Assert.Equal(expectedState, result.State);
        if (helperActive) Assert.Same(installing, result);
        else Assert.Equal("setup-interrupted", result.StatusCode);
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

    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1001")]
    public void Provisioning_manifest_accepts_well_formed_windows_sid(string ownerSid)
    {
        LocalSessionValidationResult result = LocalSessionValidation.Validate(
            new LocalSessionProvisioningManifest { OwnerSid = ownerSid });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("S-1-")]
    [InlineData("S--1-5")]
    [InlineData("S-2-5-18")]
    [InlineData("S-1-5-owner")]
    [InlineData("s-1-5-18")]
    public void Provisioning_manifest_rejects_malformed_windows_sid(string ownerSid)
    {
        LocalSessionValidationResult result = LocalSessionValidation.Validate(
            new LocalSessionProvisioningManifest { OwnerSid = ownerSid });

        Assert.False(result.IsValid);
        Assert.Contains("Owner SID is invalid.", result.Errors);
    }

    [Fact]
    public async Task Setup_helper_failure_replaces_transient_status_with_durable_problem()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-session-failure-{Guid.NewGuid():N}");
        LocalSessionPaths paths = new(root, root, Path.Combine(root, "native"));
        try
        {
            await new LocalSessionStatusStore(paths).WriteAsync(new LocalSessionStatus
            {
                State = LocalSessionState.Installing,
                StatusCode = "installing",
                Detail = "Installing.",
            });

            await new LocalSessionProvisioner(paths).RecordUnhandledFailureAsync(
                "install",
                new InvalidDataException("Owner SID is invalid."));

            LocalSessionStatus status = await new LocalSessionStatusStore(paths).ReadAsync();
            Assert.Equal(LocalSessionState.Absent, status.State);
            Assert.Equal("setup-helper-failed", status.StatusCode);
            Assert.Contains(status.Problems, problem => problem.Contains("Owner SID is invalid", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Runner_rdp_credential_uses_generic_windows_credential_type()
    {
        Assert.Equal(1, RunnerCredentialManager.CredentialType);
        System.ComponentModel.Win32Exception error = RunnerCredentialManager.CredentialError(
            87,
            "Credential write failed.");
        Assert.Contains("Win32 87", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_skips_term_service_when_setup_never_reached_machine_mutation()
    {
        LocalSessionProvisioningManifest accountOnly = new()
        {
            OwnerSid = "S-1-5-21-100",
            CompletedSteps = ["native-preflight-passed", "account-created"],
        };

        Assert.False(LocalSessionProvisioner.RequiresTermServiceRestore(accountOnly, []));
        Assert.True(LocalSessionProvisioner.RequiresTermServiceRestore(
            accountOnly with { CompletedSteps = [.. accountOnly.CompletedSteps, "term-service-mutation-started"] },
            []));
        Assert.True(LocalSessionProvisioner.RequiresTermServiceRestore(accountOnly, ["changed"]));
    }

    [Fact]
    public void Term_service_restart_stops_the_active_windows_dependency_first()
    {
        Assert.Equal(["UmRdpService", "TermService"], TermServiceConfigurationManager.RestartStopOrder);
    }

    [Fact]
    public void Firewall_isolation_requires_the_exact_enabled_inbound_block_rule()
    {
        FirewallRuleObservation tcp = new(
            FirewallIsolationManager.TcpRule,
            Enabled: true,
            Direction: 1,
            Action: 0,
            Protocol: 6,
            LocalPorts: TermServiceConfigurationManager.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Profiles: int.MaxValue,
            RemoteAddresses: "*");

        Assert.True(FirewallIsolationManager.IsExpectedIsolationRule(tcp, FirewallIsolationManager.TcpRule, 6));
        Assert.False(FirewallIsolationManager.IsExpectedIsolationRule(tcp with { Action = 1 }, FirewallIsolationManager.TcpRule, 6));
        Assert.False(FirewallIsolationManager.IsExpectedIsolationRule(tcp with { RemoteAddresses = "LocalSubnet" }, FirewallIsolationManager.TcpRule, 6));
    }

    [Fact]
    public void Missing_scheduled_task_is_an_idempotent_cleanup_success()
    {
        Assert.True(RunnerScheduledTaskManager.IsMissingTaskFailure(new FileNotFoundException()));
        Assert.True(RunnerScheduledTaskManager.IsMissingTaskFailure(
            new COMException("Task not found.", unchecked((int)0x80070002))));
        Assert.False(RunnerScheduledTaskManager.IsMissingTaskFailure(new IOException("Disk failure.")));
    }

    [Fact]
    public void Runner_task_uses_the_machine_qualified_local_account()
    {
        Assert.Equal(
            @"LILAC-TEST\LilacMacroRunner",
            RunnerScheduledTaskManager.QualifyLocalAccount("LilacMacroRunner", "LILAC-TEST"));
    }

    [Fact]
    public async Task Runner_profile_failure_round_trips_for_elevated_setup_diagnostics()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-profile-failure-{Guid.NewGuid():N}");
        LocalSessionPaths paths = new(root, root, Path.Combine(root, "native"));
        try
        {
            RunnerProfileFailure failure = new()
            {
                FailureCode = "profile-policy-access-denied",
                Detail = "A runner-scoped registry value was rejected.",
            };

            RunnerProfileStore store = new(paths);
            await store.WriteFailureAsync(failure);

            RunnerProfileFailure? restored = await store.ReadFailureAsync();
            Assert.NotNull(restored);
            Assert.Equal(failure.FailureCode, restored!.FailureCode);
            Assert.Equal(failure.Detail, restored.Detail);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
