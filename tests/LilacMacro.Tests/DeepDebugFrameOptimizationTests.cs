using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Tests;

public sealed class DeepDebugFrameOptimizationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LilacMacro.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Lossy_replacement_is_rejected_when_frame_becomes_error_evidence()
    {
        Directory.CreateDirectory(_root);
        DateTimeOffset started = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        string framePath = RecordFrame(retention, started);
        CoordinatedCodec codec = new();

        Task<bool> optimization = retention.OptimizeNextReadyFrameAsync(
            codec,
            started.AddSeconds(11),
            long.MaxValue,
            finalizing: false,
            CancellationToken.None);
        await codec.LossyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        retention.ObserveEvent(
            "macro",
            "runtime_error",
            new { Error = "failure" },
            started.AddSeconds(10));
        codec.ReleaseLossy.TrySetResult();

        Assert.True(await optimization);
        DeepDebugEvidenceFrame frame = Assert.Single(retention.Frames);
        Assert.True(frame.IsImportant);
        Assert.Equal("png", frame.Format);
        Assert.Equal("importance-upgraded-before-replacement", frame.Validation);
        Assert.True(File.Exists(framePath));

        Assert.True(await retention.OptimizeNextReadyFrameAsync(
            codec,
            started.AddSeconds(11),
            long.MaxValue,
            finalizing: true,
            CancellationToken.None));
        Assert.Equal("avif", frame.Format);
        Assert.Equal("lossless", frame.EncodingMode);
        Assert.False(File.Exists(Path.ChangeExtension(framePath, ".jpeg")));
    }

    [Fact]
    public async Task Stale_encoded_frame_is_not_reclassified_as_transition_evidence()
    {
        Directory.CreateDirectory(_root);
        DateTimeOffset started = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        RecordFrame(retention, started);
        CoordinatedCodec codec = new(releaseImmediately: true);

        Assert.True(await retention.OptimizeNextReadyFrameAsync(
            codec,
            started.AddSeconds(11),
            long.MaxValue,
            finalizing: false,
            CancellationToken.None));
        retention.ObserveEvent("macro", "initialize_completed", null, started.AddSeconds(30));
        RecordFrame(retention, started.AddSeconds(30));

        DeepDebugEvidenceFrame[] frames = retention.Frames.ToArray();
        Assert.Equal("jpeg", frames[0].Format);
        Assert.False(frames[0].IsImportant);
        Assert.True(frames[1].IsImportant);
    }

    [Fact]
    public async Task Completion_drain_cancels_slow_encoder_at_deadline_and_keeps_png()
    {
        Directory.CreateDirectory(_root);
        DateTimeOffset started = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        DeepDebugEvidenceRetention retention = new();
        string framePath = RecordFrame(retention, started);
        retention.ObserveEvent("macro", "runtime_error", new { Error = "failure" }, started);
        BlockingCodec codec = new();
        using DeepDebugFrameOptimizationWorker worker = new(
            retention,
            codec,
            long.MaxValue,
            TimeSpan.FromMilliseconds(50));
        worker.Start();
        worker.Signal(started.AddSeconds(11));
        await codec.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        DeepDebugOptimizationMetrics metrics = await worker.CompleteAsync(started.AddSeconds(12));

        Assert.True(metrics.CompletionDrainTimedOut);
        Assert.True(File.Exists(framePath));
        Assert.Equal("png", Assert.Single(retention.Frames).Format);
    }

    private string RecordFrame(DeepDebugEvidenceRetention retention, DateTimeOffset timestamp)
    {
        string path = Path.Combine(_root, $"frame-{timestamp.Ticks}.png");
        byte[] png = PngEncoder.Encode(new RgbImage(2, 2, new byte[12], true));
        File.WriteAllBytes(path, png);
        retention.RecordFrame(path, timestamp, png, fullClient: true);
        return path;
    }

    private sealed class CoordinatedCodec(bool releaseImmediately = false) : IDeepDebugFrameCodec
    {
        public TaskCompletionSource LossyStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseLossy { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DeepDebugFrameEncodingResult> EncodeAsync(
            string pngPath,
            bool lossless,
            bool waitForLease,
            CancellationToken cancellationToken = default)
        {
            if (!lossless)
            {
                LossyStarted.TrySetResult();
                if (!releaseImmediately) await ReleaseLossy.Task.WaitAsync(cancellationToken);
            }
            return new(true, [1, 2, 3], lossless ? "pixel-exact" : "decode-verified",
                lossless ? "avif" : "jpeg", lossless ? null : 14);
        }
    }

    private sealed class BlockingCodec : IDeepDebugFrameCodec
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DeepDebugFrameEncodingResult> EncodeAsync(
            string pngPath,
            bool lossless,
            bool waitForLease,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
