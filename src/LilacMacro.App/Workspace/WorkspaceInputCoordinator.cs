using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Windows;

namespace LilacMacro.App.Workspace;

internal sealed class WorkspaceInputCoordinator(
    RobloxWindowService windows,
    RobloxInputService input,
    SemaphoreSlim operationGate,
    DeepDebugSessionService deepDebug,
    Func<RobloxWindow?> getWindow,
    Action<RobloxWindow, PixelSize> updateWindow)
{
    private readonly CrossProcessRobloxInputGate _crossProcessGate =
        CrossProcessRobloxInputGate.CreateDefault();

    public Task FocusAsync(PixelSize size, CancellationToken token) => RunAsync(
        "focus", new { RequiredSize = size },
        (window, cancellation) => input.FocusClientAsync(window, size, cancellation), token);

    public Task ClickAsync(PixelSize size, PixelPoint point, CancellationToken token) => RunAsync(
        "click", new { RequiredSize = size, Point = point },
        (window, cancellation) => input.ClickClientAsync(window, size, point, cancellation), token);

    public Task HoverAsync(PixelSize size, PixelPoint point, CancellationToken token) => RunAsync(
        "hover", new { RequiredSize = size, Point = point },
        (window, cancellation) => input.HoverClientAsync(window, size, point, cancellation), token);

    public Task ScrollAsync(
        PixelSize size,
        PixelPoint point,
        int delta,
        TimeSpan duration,
        CancellationToken token) => RunAsync(
            "scroll", new { RequiredSize = size, Point = point, WheelDelta = delta, Duration = duration },
            (window, cancellation) => input.ScrollClientAsync(
                window, size, point, delta, duration, cancellation), token);

    public Task DragAsync(
        PixelSize size,
        PixelPoint start,
        PixelPoint end,
        TimeSpan duration,
        CancellationToken token) => RunAsync(
            "drag", new { RequiredSize = size, Start = start, End = end, Duration = duration },
            (window, cancellation) => input.DragClientAsync(
                window, size, start, end, duration, cancellation), token);

    public Task RunKeysAsync(PixelSize size, AutomationKeySequence sequence, CancellationToken token) => RunAsync(
        "key_sequence", new { RequiredSize = size, Sequence = sequence },
        (window, cancellation) => input.RunKeySequenceAsync(window, size, sequence, cancellation), token);

    public Task RunQuickPlacementAsync(
        PixelSize size,
        int quickKey,
        int cancelKey,
        IReadOnlyList<QuickPlacementPoint> placements,
        CancellationToken token) => RunAsync(
            "quick_placement_batch",
            new
            {
                RequiredSize = size,
                QuickPlacementVirtualKey = quickKey,
                CancelPlacementVirtualKey = cancelKey,
                Placements = placements
            },
            (window, cancellation) => input.RunQuickPlacementBatchAsync(
                window, size, quickKey, cancelKey, placements, cancellation), token);

    public Task AlignCameraAsync(PixelSize size, int shiftLockVirtualKey, CancellationToken token) => RunAsync(
        "align_camera", new { RequiredSize = size, ShiftLockVirtualKey = shiftLockVirtualKey },
        (window, cancellation) => input.AlignCameraAsync(window, size, shiftLockVirtualKey, cancellation), token);

    private async Task RunAsync(
        string action,
        object data,
        Func<RobloxWindow, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (!await operationGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another LilacMacro operation is already running.");
        try
        {
            using IDisposable processLease = _crossProcessGate.Acquire();
            RobloxWindow window = getWindow() ?? windows.FindBest()
                ?? throw new InvalidOperationException("Start Roblox in windowed mode before sending input.");
            deepDebug.RecordInput($"{action}_started", new
            {
                window.ProcessId,
                ClientSize = windows.GetClientBounds(window).Size,
                Data = data,
            });
            await operation(window, cancellationToken);
            PixelSize observed = windows.GetClientBounds(window).Size;
            updateWindow(window, observed);
            deepDebug.RecordInput($"{action}_completed", new { ObservedClientSize = observed });
        }
        finally
        {
            operationGate.Release();
        }
    }
}
