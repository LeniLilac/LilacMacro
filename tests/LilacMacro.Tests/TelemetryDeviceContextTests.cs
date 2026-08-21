using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Services;
using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class TelemetryDeviceContextTests
{
    [Theory]
    [InlineData("AMD Ryzen 9 9950X 16-Core Processor", "AMD Ryzen 9 9950X 16-Core Processor")]
    [InlineData("intel(R) Core(TM) Ultra 9 285K", "Intel Core Ultra 9 285K")]
    [InlineData("NVIDIA GeForce RTX 4090", "NVIDIA GeForce RTX 4090")]
    [InlineData("Qualcomm   Snapdragon(R) X Elite", "Qualcomm Snapdragon X Elite")]
    [InlineData("Untrusted Device 123", "unknown")]
    [InlineData("", "unknown")]
    public void Hardware_models_are_normalized_to_known_public_vendor_names(
        string source,
        string expected)
    {
        string normalized = WindowsTelemetryDeviceContextProvider.NormalizeModelForTelemetry(source);

        Assert.Equal(expected, normalized);
        Assert.True(ProductTelemetryPolicy.IsHardwareModel(normalized));
    }

    [Fact]
    public void Ocr_timing_maps_to_the_active_processor_or_graphics_model()
    {
        ProductTelemetryDeviceContext device = new(
            "AMD Ryzen 9 9950X",
            "NVIDIA GeForce RTX 4090",
            1920,
            1080);

        ProductTelemetryEvent cpu = Assert.IsType<ProductTelemetryEvent>(ProductTelemetryService.Map(
            Observation("ocr", "inference_completed", new { InferenceMilliseconds = 68, Device = "cpu" }),
            device));
        ProductTelemetryEvent gpu = Assert.IsType<ProductTelemetryEvent>(ProductTelemetryService.Map(
            Observation("ocr", "inference_completed", new { InferenceMilliseconds = 31, Device = "gpu:0" }),
            device));

        Assert.Equal("AMD Ryzen 9 9950X", cpu.HardwareModel);
        Assert.Equal("NVIDIA GeForce RTX 4090", gpu.HardwareModel);
        ProductTelemetryPolicy.Validate(new ProductTelemetryBatch(
            Guid.NewGuid(), "1.0.154", ProductTelemetryPolicy.CurrentPrivacyNoticeVersion, [cpu, gpu]));
    }

    [Fact]
    public void Ui_scale_feedback_maps_primary_display_and_render_pair()
    {
        ProductTelemetryDeviceContext device = new("unknown", "unknown", 1920, 1080);

        ProductTelemetryEvent item = Assert.IsType<ProductTelemetryEvent>(ProductTelemetryService.Map(
            Observation("ui_scale", "ui_scale_feedback", new
            {
                Candidate = 1.0,
                ObservedRenderedScale = 0.997,
            }),
            device));

        Assert.Equal(ProductTelemetryKind.UiScaleCalibration, item.Kind);
        Assert.Equal(1920, item.DisplayWidth);
        Assert.Equal(1080, item.DisplayHeight);
        Assert.Equal(1000, item.InputScaleMilli);
        Assert.Equal(997, item.RenderedScaleMilli);
        ProductTelemetryPolicy.Validate(new ProductTelemetryBatch(
            Guid.NewGuid(), "1.0.154", ProductTelemetryPolicy.CurrentPrivacyNoticeVersion, [item]));
    }

    [Fact]
    public void Telemetry_notice_accepts_supported_history_and_rejects_future_versions()
    {
        ProductTelemetryEvent item = new(
            ProductTelemetryKind.SessionStarted,
            DateTimeOffset.UtcNow,
            Feature: "macro",
            Outcome: "started",
            OperatingSystem: "windows-11.0",
            LogicalProcessorCount: 16,
            GraphicsCapability: "not-observed");

        ProductTelemetryPolicy.Validate(new ProductTelemetryBatch(Guid.NewGuid(), "1.0.154", 1, [item]));
        Assert.Throws<InvalidDataException>(() => ProductTelemetryPolicy.Validate(
            new ProductTelemetryBatch(
                Guid.NewGuid(),
                "1.0.154",
                ProductTelemetryPolicy.CurrentPrivacyNoticeVersion + 1,
                [item])));
    }

    private static DeepDebugObservation Observation(string category, string action, object data) =>
        new(DateTimeOffset.UtcNow, category, action, data, null);
}
