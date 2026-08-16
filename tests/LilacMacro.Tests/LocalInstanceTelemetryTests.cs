using System.Text.Json;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.Tests;

public sealed class LocalInstanceTelemetryTests
{
    [Theory]
    [InlineData("preflight-rejected", "preflight-rejected")]
    [InlineData("setup-failed-rolled-back", "setup-rolled-back")]
    [InlineData("cleanup-incomplete", "cleanup-incomplete")]
    [InlineData("instance-manager-ready", "operation-incomplete")]
    public void Status_codes_map_to_bounded_failure_codes(string statusCode, string expected)
    {
        Assert.Equal(
            expected,
            LocalInstanceFailurePolicy.Classify(
                "setup",
                statusCode,
                new InvalidOperationException("local detail"),
                1,
                helperStarted: true));
    }

    [Fact]
    public void Local_instance_failure_observation_maps_without_free_form_data()
    {
        ProductTelemetryEvent? item = ProductTelemetryService.Map(new DeepDebugObservation(
            DateTimeOffset.UtcNow,
            "local_instance",
            "operation_failed",
            new
            {
                Operation = "setup",
                FailureCode = "preflight-rejected",
                ConfigurationMode = "not-applicable",
                DurationMilliseconds = 1_250,
                ProcessExitCode = 1,
                RunnerCount = 0,
                Error = "C:\\Users\\name\\private-detail",
            },
            null));

        Assert.NotNull(item);
        Assert.Equal(ProductTelemetryKind.LocalInstanceFailure, item!.Kind);
        Assert.Equal("setup", item.Operation);
        Assert.Equal("preflight-rejected", item.Outcome);
        Assert.Equal("not-applicable", item.ConfigurationMode);
        Assert.DoesNotContain("private-detail", JsonSerializer.Serialize(item), StringComparison.Ordinal);
        ProductTelemetryPolicy.Validate(new ProductTelemetryBatch(
            Guid.NewGuid(), "1.2.3", 1, [item]));
    }

    [Fact]
    public async Task Local_instance_failure_rate_limit_persists_by_scope()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ProductTelemetryEvent item = new(
            ProductTelemetryKind.LocalInstanceFailure,
            DateTimeOffset.UtcNow,
            Feature: "local-instance",
            Outcome: "preflight-rejected",
            DurationMilliseconds: 10,
            OperatingSystem: "windows-11.0",
            Operation: "setup",
            FailureCode: "preflight-rejected",
            ConfigurationMode: "not-applicable",
            RunnerCount: 0);
        try
        {
            ProductTelemetryRateLimitStore first = new(root);
            await first.LoadAsync();
            Assert.False(first.WasSent("1.2.3", item));
            await first.MarkSentAsync("1.2.3", [item]);

            ProductTelemetryRateLimitStore second = new(root);
            await second.LoadAsync();
            Assert.True(second.WasSent("1.2.3", item));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
