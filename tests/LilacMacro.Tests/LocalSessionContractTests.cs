using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.LocalSession;
using LilacMacro.Windows;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.Tests;

public sealed class LocalSessionContractTests
{
    [Fact]
    public async Task Every_ui_instance_executes_on_its_own_desktop()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-local-{Guid.NewGuid():N}");
        try
        {
            MacroOwnerState state = await MacroOwnerState.LoadAsync(
                new MacroSettingsStore(root),
                new DpapiSecretProtector());

            Assert.Equal(ExecutionTarget.LocalDesktop, state.ExecutionTarget);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("install")]
    [InlineData("repair")]
    [InlineData("remove")]
    [InlineData("uninstall-cleanup")]
    [InlineData("add-shared")]
    [InlineData("add-isolated")]
    [InlineData("remove-profile")]
    [InlineData("relaunch-update")]
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
    public void Setup_helper_bounds_profile_removal_arguments()
    {
        Assert.True(LocalSessionSetupVerbPolicy.AreArgumentsAllowed(["remove-profile", "runner-2"]));
        Assert.False(LocalSessionSetupVerbPolicy.AreArgumentsAllowed(["remove-profile"]));
        Assert.False(LocalSessionSetupVerbPolicy.AreArgumentsAllowed(["remove-profile", "../owner"]));
        Assert.False(LocalSessionSetupVerbPolicy.AreArgumentsAllowed(["add-shared", "runner-2"]));
        Assert.True(LocalSessionSetupVerbPolicy.AreArgumentsAllowed(["relaunch-update", @"C:\Users\owner\AppData\Local\LilacMacro\updates\id\update-state.txt"]));
        Assert.False(LocalSessionSetupVerbPolicy.AreArgumentsAllowed(["relaunch-update", "bad\npath"]));
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
        Assert.Equal("/v:127.0.0.2:33991 /f", start.Arguments);
        Assert.True(start.UseShellExecute);
    }

    [Fact]
    public void Each_runner_uses_a_distinct_bounded_rdp_endpoint_and_ui_task()
    {
        string rdpRoot = Path.Combine(Path.GetTempPath(), $"lilac-rdp-{Guid.NewGuid():N}");
        LocalRunnerProfile runner2 = LocalRunnerProfileProvisioner.Create(2, RunnerConfigurationMode.Isolated) with
        {
            RunnerSid = "S-1-5-21-200",
        };
        try
        {
            System.Diagnostics.ProcessStartInfo start = LocalInstanceManagerController.CreateRdpStartInfo(
                runner2,
                rdpRoot,
                MacroLayoutProfile.Compact1366x768);
            string arguments = RunnerScheduledTaskManager.CreateArguments(runner2, @"C:\ProgramData\LilacMacro\Configurations\runner-2");
            string rdpPath = Path.Combine(rdpRoot, "runner-2.rdp");
            string rdp = File.ReadAllText(rdpPath);
            byte[] rdpBytes = File.ReadAllBytes(rdpPath);

            Assert.Equal($"\"{rdpPath}\"", start.Arguments);
            Assert.Equal(new byte[] { 0xff, 0xfe }, rdpBytes[..2]);
            Assert.Contains("full address:s:127.0.0.3:33991", rdp, StringComparison.Ordinal);
            Assert.Contains($"username:s:{Environment.MachineName}\\LilacMacroRunner2", rdp, StringComparison.Ordinal);
            Assert.Contains("authentication level:i:0", rdp, StringComparison.Ordinal);
            Assert.Contains("enablecredsspsupport:i:1", rdp, StringComparison.Ordinal);
            Assert.Contains("screen mode id:i:1", rdp, StringComparison.Ordinal);
            Assert.Contains("desktopwidth:i:1366", rdp, StringComparison.Ordinal);
            Assert.Contains("desktopheight:i:768", rdp, StringComparison.Ordinal);
            Assert.Contains("dynamic resolution:i:0", rdp, StringComparison.Ordinal);
            Assert.Contains("redirectclipboard:i:0", rdp, StringComparison.Ordinal);
            Assert.Contains("redirectwebauthn:i:0", rdp, StringComparison.Ordinal);
            Assert.Contains("drivestoredirect:s:", rdp, StringComparison.Ordinal);
            Assert.Contains("--managed-instance runner-2", arguments, StringComparison.Ordinal);
            Assert.Contains("--instance-name \"Runner 2\"", arguments, StringComparison.Ordinal);
            Assert.Contains("--configuration-mode isolated", arguments, StringComparison.Ordinal);
            Assert.Equal("LilacMacro Instance runner-2", RunnerScheduledTaskManager.TaskNameFor(runner2.Id));
        }
        finally
        {
            if (Directory.Exists(rdpRoot)) Directory.Delete(rdpRoot, recursive: true);
        }
    }

    [Fact]
    public void Full_runner_viewport_requests_1920_by_1080()
    {
        LocalRunnerProfile profile = LocalRunnerProfileProvisioner.Create(1, RunnerConfigurationMode.Shared);

        string rdp = LocalInstanceManagerController.CreateRdpProfileContent(
            profile,
            MacroLayoutProfile.Full1920x1080);

        Assert.Contains("desktopwidth:i:1920", rdp, StringComparison.Ordinal);
        Assert.Contains("desktopheight:i:1080", rdp, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_instance_mutex_is_scoped_to_the_runner_profile()
    {
        Assert.Equal(
            @"Local\LilacMacro.ManagedInstance.runner-2",
            global::LilacMacro.App.App.ManagedInstanceMutexName("runner-2"));
        Assert.NotEqual(
            global::LilacMacro.App.App.ManagedInstanceMutexName("runner-1"),
            global::LilacMacro.App.App.ManagedInstanceMutexName("runner-2"));
    }

    [Fact]
    public void Failed_profile_mutation_does_not_report_a_stale_instance_count_as_the_error()
    {
        LocalInstanceManagerSnapshot snapshot = new(
            new LocalSessionStatus
            {
                State = LocalSessionState.Ready,
                StatusCode = "instance-manager-ready",
                Detail = "0 local macro instance(s) configured.",
            },
            []);

        string detail = LocalInstanceManagerController.OperationFailureDetail(snapshot);

        Assert.Equal("The local instance operation did not complete. Run Repair and retry it.", detail);
    }

    [Fact]
    public void Interactive_session_rejects_nonowned_rdp_destinations()
    {
        LocalRunnerProfile profile = LocalRunnerProfileProvisioner.Create(2, RunnerConfigurationMode.Shared) with
        {
            LoopbackAddress = "192.0.2.20",
        };

        Assert.Throws<InvalidDataException>(() => LocalInstanceManagerController.CreateRdpProfileContent(profile));
    }

    [Fact]
    public void Shared_and_isolated_profiles_have_distinct_configuration_and_credential_scopes()
    {
        LocalSessionPaths paths = new(@"C:\ProgramData\LilacMacro", @"C:\Program Files\LilacMacro", "native");
        LocalRunnerProfile shared = LocalRunnerProfileProvisioner.Create(1, RunnerConfigurationMode.Shared) with
        {
            RunnerSid = "S-1-5-21-101",
        };
        LocalRunnerProfile isolated = LocalRunnerProfileProvisioner.Create(2, RunnerConfigurationMode.Isolated) with
        {
            RunnerSid = "S-1-5-21-102",
        };

        Assert.Equal(paths.SharedConfigurationRoot, paths.ConfigurationRootFor(shared));
        Assert.Equal(Path.Combine(paths.ConfigurationsRoot, "runner-2"), paths.ConfigurationRootFor(isolated));
        Assert.Equal("TERMSRV/127.0.0.2", paths.CredentialTargetFor(shared));
        Assert.Equal("TERMSRV/127.0.0.3", paths.CredentialTargetFor(isolated));
        Assert.NotEqual(paths.SecretCredentialTargetFor(shared), paths.SecretCredentialTargetFor(isolated));
    }

    [Fact]
    public void Manifest_rejects_duplicate_or_unowned_runner_profiles()
    {
        LocalRunnerProfile runner = LocalRunnerProfileProvisioner.Create(1, RunnerConfigurationMode.Shared) with
        {
            RunnerSid = "S-1-5-21-101",
        };
        LocalSessionProvisioningManifest duplicate = new()
        {
            OwnerSid = "S-1-5-21-100",
            RunnerProfiles = [runner, runner],
        };
        LocalSessionProvisioningManifest unowned = duplicate with
        {
            RunnerProfiles = [runner with { AccountName = "Administrator" }],
        };

        Assert.False(LocalSessionValidation.Validate(duplicate).IsValid);
        Assert.False(LocalSessionValidation.Validate(unowned).IsValid);
        Assert.True(LocalSessionValidation.Validate(duplicate with { RunnerProfiles = [runner] }).IsValid);
    }

    [Fact]
    public void Empty_multi_instance_manifest_does_not_recreate_removed_runner()
    {
        LocalSessionProvisioningManifest manifest = new()
        {
            RunnerAccountName = "LilacMacroRunner",
            RunnerSid = string.Empty,
            RunnerProfiles = [],
        };

        IReadOnlyList<LocalRunnerProfile> profiles = LocalSessionProfileCompatibility.ResolveProfiles(manifest);
        LocalSessionProvisioningManifest? normalized = LocalSessionProfileCompatibility.NormalizeManifest(manifest);

        Assert.Empty(profiles);
        Assert.NotNull(normalized);
        Assert.Empty(normalized.RunnerProfiles);
    }

    [Fact]
    public void Legacy_single_runner_manifest_is_still_migrated()
    {
        LocalSessionProvisioningManifest manifest = new()
        {
            RunnerAccountName = "LilacMacroRunner",
            RunnerSid = "S-1-5-21-101",
            RunnerProfiles = [],
        };

        LocalRunnerProfile profile = Assert.Single(LocalSessionProfileCompatibility.ResolveProfiles(manifest));
        LocalSessionProvisioningManifest? normalized = LocalSessionProfileCompatibility.NormalizeManifest(manifest);

        Assert.Equal("runner-1", profile.Id);
        Assert.Equal("Runner 1", profile.DisplayName);
        Assert.Equal(manifest.RunnerSid, profile.RunnerSid);
        Assert.NotNull(normalized);
        Assert.Single(normalized.RunnerProfiles);
    }

    [Fact]
    public void Worker_process_path_uses_limited_information_query()
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
        string ownerSid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;

        RunnerProcessAccessManager.GrantOwnerValidationAccess(ownerSid);

        Assert.Equal(
            Path.GetFullPath(Environment.ProcessPath!),
            Path.GetFullPath(SessionPipeClient.ReadProcessPath(process.Handle)),
            ignoreCase: true);
        Assert.Equal(0x1000u, RunnerProcessAccessManager.OwnerProcessQueryAccess);
        Assert.Equal(0x0008u, RunnerProcessAccessManager.OwnerTokenQueryAccess);
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
    public void Runner_rdp_credential_uses_the_generic_type_with_domain_password_migration()
    {
        Assert.Equal(1, RunnerCredentialManager.RdpCredentialType);
        Assert.Equal(1, RunnerCredentialManager.SecretCredentialType);
        Assert.Equal(2, RunnerCredentialManager.LegacyRdpCredentialType);
        Assert.Equal(
            @"LILAC-TEST\LilacMacroRunner",
            RunnerScheduledTaskManager.QualifyLocalAccount("LilacMacroRunner", "LILAC-TEST"));
        LocalSessionPaths paths = new("program-data", "install", "native");
        Assert.Equal("TERMSRV/127.0.0.2", paths.CredentialTarget);
        Assert.Equal("TERMSRV/127.0.0.2:33991", paths.PortCredentialTarget);
        System.ComponentModel.Win32Exception error = RunnerCredentialManager.CredentialError(
            87,
            "Credential write failed.");
        Assert.Contains("Win32 87", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutable_worker_status_is_separate_from_the_read_only_provisioning_journal()
    {
        LocalSessionPaths paths = new("program-data", "install", "native");

        Assert.Equal(Path.Combine(paths.SessionRoot, "provisioning.json"), paths.JournalPath);
        Assert.Equal(Path.Combine(paths.RunnerRoot, "status.json"), paths.StatusPath);
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
    public void Broken_rdp_certificate_is_journaled_for_replacement_and_exact_restoration()
    {
        const string brokenThumbprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        OriginalSystemValue[] baseline =
        [
            new(RemoteDesktopCertificateManager.BaselineKind, "Remote Desktop", false, null, null),
            new(
                RemoteDesktopCertificateManager.CertificateKind,
                brokenThumbprint,
                true,
                RemoteDesktopCertificateManager.MissingKeyCertificateType,
                Convert.ToBase64String([1, 2, 3])),
        ];

        Assert.Equal([brokenThumbprint], RemoteDesktopCertificateManager.BrokenBaselineThumbprints(baseline));
        Assert.Empty(RemoteDesktopCertificateManager.CompareBaseline(
            baseline,
            [new RemoteDesktopCertificateObservation(brokenThumbprint, false, Convert.ToBase64String([1, 2, 3]))]));
        Assert.Contains(
            RemoteDesktopCertificateManager.CompareBaseline(
                baseline,
                [new RemoteDesktopCertificateObservation(new string('B', 40), true, Convert.ToBase64String([4, 5, 6]))]),
            problem => problem.Contains("Generated RDP certificate remains", StringComparison.Ordinal));
    }

    [Fact]
    public void Provisioning_manifest_rejects_malformed_rdp_certificate_journal()
    {
        LocalSessionProvisioningManifest manifest = new()
        {
            OwnerSid = "S-1-5-21-100",
            OriginalSystemState =
            [
                new("rdp-certificate", "not-a-thumbprint", true, "x509-der-private-key-missing", "not-base64"),
            ],
        };

        LocalSessionValidationResult result = LocalSessionValidation.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("certificate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Term_service_restart_stops_the_active_windows_dependency_first()
    {
        Assert.Equal(["UmRdpService", "TermService"], TermServiceConfigurationManager.RestartStopOrder);
    }

    [Theory]
    [InlineData(0, 10_000, 500)]
    [InlineData(1_000, 10_000, 500)]
    [InlineData(8_000, 10_000, 800)]
    [InlineData(30_000, 10_000, 2_000)]
    [InlineData(30_000, 275, 275)]
    public void Term_service_polling_honors_wait_hints_with_bounded_delays(
        uint waitHintMilliseconds,
        long remainingMilliseconds,
        int expectedDelayMilliseconds)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedDelayMilliseconds),
            TermServiceConfigurationManager.CalculatePollDelay(waitHintMilliseconds, remainingMilliseconds));
        Assert.Equal(TimeSpan.FromSeconds(60), TermServiceConfigurationManager.ServiceTransitionTimeout);
    }

    [Theory]
    [InlineData(1, "Stopped")]
    [InlineData(3, "Stop Pending")]
    [InlineData(4, "Running")]
    [InlineData(99, "Unknown (99)")]
    public void Term_service_diagnostics_name_service_states(uint state, string expected)
    {
        Assert.Equal(expected, TermServiceConfigurationManager.StateName(state));
    }

    [Theory]
    [InlineData(4, 1, 1, 1_999, false)]
    [InlineData(4, 1, 1, 2_000, true)]
    [InlineData(4, 1, 2, 30_000, true)]
    [InlineData(4, 1, 3, 30_000, false)]
    [InlineData(4, 0, 1, 30_000, false)]
    [InlineData(3, 1, 1, 30_000, false)]
    [InlineData(1, 1, 1, 30_000, false)]
    public void Term_service_stop_retries_only_a_bounded_running_bounce(
        uint currentState,
        uint controlsAccepted,
        int stopRequests,
        long millisecondsSinceAcceptedStop,
        bool expected)
    {
        Assert.Equal(
            expected,
            TermServiceConfigurationManager.ShouldRetryStop(
                currentState,
                controlsAccepted,
                stopRequests,
                millisecondsSinceAcceptedStop));
        Assert.Equal(3, TermServiceConfigurationManager.MaximumStopRequests);
        Assert.Equal(TimeSpan.FromSeconds(2), TermServiceConfigurationManager.StopRetryObservationInterval);
    }

    [Theory]
    [InlineData("TermService", 1051, 0, true)]
    [InlineData("TermService", 1051, 2, true)]
    [InlineData("TermService", 1051, 3, false)]
    [InlineData("TermService", 1061, 0, false)]
    [InlineData("UmRdpService", 1051, 0, false)]
    public void Term_service_bounce_restops_only_the_known_dependent(
        string serviceName,
        int error,
        int dependentRestops,
        bool expected)
    {
        Assert.Equal(
            expected,
            TermServiceConfigurationManager.ShouldRestopKnownDependent(serviceName, error, dependentRestops));
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
            RemoteAddresses: FirewallIsolationManager.ExternalRemoteAddresses);

        Assert.True(FirewallIsolationManager.IsExpectedIsolationRule(tcp, FirewallIsolationManager.TcpRule, 6));
        Assert.True(FirewallIsolationManager.IsExpectedIsolationRule(
            tcp with { RemoteAddresses = string.Join(',', FirewallIsolationManager.ExternalRemoteAddresses.Split(',').Reverse()) },
            FirewallIsolationManager.TcpRule,
            6));
        Assert.False(FirewallIsolationManager.IsExpectedIsolationRule(tcp with { Action = 1 }, FirewallIsolationManager.TcpRule, 6));
        Assert.False(FirewallIsolationManager.IsExpectedIsolationRule(tcp with { RemoteAddresses = "LocalSubnet" }, FirewallIsolationManager.TcpRule, 6));
        Assert.False(FirewallIsolationManager.IsExpectedIsolationRule(tcp with { RemoteAddresses = "*" }, FirewallIsolationManager.TcpRule, 6));
        Assert.False(FirewallIsolationManager.IsExpectedIsolationRule(
            tcp with { RemoteAddresses = $"{FirewallIsolationManager.ExternalRemoteAddresses},127.0.0.1" },
            FirewallIsolationManager.TcpRule,
            6));
    }

    [Fact]
    public void Firewall_isolation_excludes_only_the_authorized_ipv4_loopback_endpoint()
    {
        string[] scope = FirewallIsolationManager.ExternalRemoteAddresses.Split(',');

        Assert.DoesNotContain("127.0.0.1", scope);
        Assert.Contains("0.0.0.0-127.0.0.0", scope);
        Assert.Contains("127.0.0.2-255.255.255.255", scope);
        Assert.Contains("::-::", scope);
        Assert.Contains("::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", scope);
        Assert.Equal(TimeSpan.FromSeconds(15), FirewallIsolationManager.ListenerReadyTimeout);
    }

    [Theory]
    [InlineData(false, "The RDP listener did not accept 127.0.0.2:33991 within 15 seconds after restart.", true)]
    [InlineData(false, "The TCP isolation rule is missing.", false)]
    [InlineData(true, "", false)]
    public void Only_a_bounded_listener_startup_delay_authorizes_restart_required_state(
        bool passed,
        string problem,
        bool expected)
    {
        Assert.Equal(
            expected,
            FirewallIsolationManager.IsListenerStartupDelay(new FirewallIsolationVerification(passed, problem)));
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
        Assert.Equal(3, RunnerScheduledTaskManager.InteractiveTokenLogonType);
        Assert.Equal(9, RunnerScheduledTaskManager.LogonTriggerType);
        Assert.Equal(11, RunnerScheduledTaskManager.SessionStateChangeTriggerType);
        Assert.Equal(3, RunnerScheduledTaskManager.RemoteConnectStateChange);
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
