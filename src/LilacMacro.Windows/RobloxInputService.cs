using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

public sealed class RobloxInputService(RobloxWindowService windows)
{
    public async Task FocusClientAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        CancellationToken cancellationToken = default)
    {
        _ = await PrepareWindowAsync(window, expectedSize, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClickClientAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        PixelPoint point,
        CancellationToken cancellationToken = default)
    {
        ClientBounds client = await PrepareAsync(window, expectedSize, point, cancellationToken).ConfigureAwait(false);
        client = await RobloxClickCursorAcquirer.AcquireAsync(
            client,
            () => PrepareAsync(window, expectedSize, point, cancellationToken),
            current => MoveCursorWithRegisteredMotion(current, point),
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(RobloxInputProtocol.ClickPositionSettleMilliseconds, cancellationToken).ConfigureAwait(false);
        client = await RobloxClickCursorAcquirer.AcquireAsync(
            client,
            () => PrepareAsync(window, expectedSize, point, cancellationToken),
            current => MoveCursorWithRegisteredMotion(current, point),
            cancellationToken).ConfigureAwait(false);
        NativeInputMethods.mouse_event(NativeInputMethods.MouseLeftDown, 0, 0, 0, 0);
        try
        {
            await Task.Delay(RobloxInputProtocol.ClickHoldMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            NativeInputMethods.mouse_event(NativeInputMethods.MouseLeftUp, 0, 0, 0, 0);
        }
        await ParkCursorWithAcknowledgedMotionAsync(client, cancellationToken).ConfigureAwait(false);
        await Task.Delay(RobloxInputProtocol.HoverRenderSettleMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    public async Task HoverClientAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        PixelPoint point,
        CancellationToken cancellationToken = default)
    {
        ClientBounds client = await PrepareAsync(window, expectedSize, point, cancellationToken).ConfigureAwait(false);
        MoveCursorWithRegisteredMotion(client, point);
        await Task.Delay(RobloxInputProtocol.HoverRenderSettleMilliseconds, cancellationToken).ConfigureAwait(false);
        await VerifyClientSizeAsync(window, expectedSize, "hover", cancellationToken).ConfigureAwait(false);
    }

    public async Task ScrollClientAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        PixelPoint point,
        int wheelDelta,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (wheelDelta == 0) throw new ArgumentOutOfRangeException(nameof(wheelDelta));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        ClientBounds client = await PrepareAsync(window, expectedSize, point, cancellationToken).ConfigureAwait(false);
        MoveCursorWithRegisteredMotion(client, point);
        await Task.Delay(RobloxInputProtocol.ClickPositionSettleMilliseconds, cancellationToken).ConfigureAwait(false);
        await SendWheelOverTimeAsync(wheelDelta, duration, cancellationToken).ConfigureAwait(false);
        await VerifyClientSizeAsync(window, expectedSize, "scroll", cancellationToken).ConfigureAwait(false);
    }

    public async Task DragClientAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        PixelPoint start,
        PixelPoint end,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        _ = await PrepareAsync(window, expectedSize, end, cancellationToken).ConfigureAwait(false);
        ClientBounds client = await PrepareAsync(window, expectedSize, start, cancellationToken).ConfigureAwait(false);
        MoveCursorWithRegisteredMotion(client, start);
        await Task.Delay(RobloxInputProtocol.ClickPositionSettleMilliseconds, cancellationToken).ConfigureAwait(false);
        NativeInputMethods.mouse_event(NativeInputMethods.MouseLeftDown, 0, 0, 0, 0);
        try
        {
            Stopwatch clock = Stopwatch.StartNew();
            for (int index = 1; index <= RobloxInputProtocol.ScrollbarDragIncrementCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double progress = index / (double)RobloxInputProtocol.ScrollbarDragIncrementCount;
                PixelPoint point = new(
                    checked((int)Math.Round(start.X + (end.X - start.X) * progress)),
                    checked((int)Math.Round(start.Y + (end.Y - start.Y) * progress)));
                MoveCursorWithRegisteredMotion(client, point);
                await DelayUntilAsync(clock, duration * progress, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            NativeInputMethods.mouse_event(NativeInputMethods.MouseLeftUp, 0, 0, 0, 0);
        }
        await ParkCursorWithAcknowledgedMotionAsync(client, cancellationToken).ConfigureAwait(false);
        await VerifyClientSizeAsync(window, expectedSize, "drag", cancellationToken).ConfigureAwait(false);
    }

    public async Task RunKeySequenceAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        AutomationKeySequence sequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        PixelPoint center = ClientCenter(expectedSize);
        for (int index = 0; index < sequence.Steps.Count; index++)
        {
            AutomationKeyPress step = sequence.Steps[index];
            _ = await PrepareAsync(window, expectedSize, center, cancellationToken).ConfigureAwait(false);
            await RobloxKeyboardInput.HoldKeyAsync(step, cancellationToken).ConfigureAwait(false);
            if (index + 1 < sequence.Steps.Count)
            {
                await Task.Delay(RobloxInputProtocol.InterKeyDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        await VerifyClientSizeAsync(window, expectedSize, "key chain", cancellationToken).ConfigureAwait(false);
    }

    public async Task RunTextInputAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        string value,
        CancellationToken cancellationToken = default)
    {
        _ = AutomationTextInput.Create(value, capsLockEnabled: false);
        _ = await PrepareWindowAsync(window, expectedSize, cancellationToken).ConfigureAwait(false);
        await RobloxKeyboardInput.SendTextAsync(value, cancellationToken).ConfigureAwait(false);
        await VerifyClientSizeAsync(window, expectedSize, "text input", cancellationToken).ConfigureAwait(false);
    }

    public async Task RunQuickPlacementBatchAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        int quickPlacementVirtualKey,
        int cancelPlacementVirtualKey,
        IReadOnlyList<QuickPlacementPoint> placements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placements);
        if (placements.Count == 0) return;
        foreach (QuickPlacementPoint placement in placements) placement.Validate(expectedSize);

        PixelPoint center = ClientCenter(expectedSize);
        ClientBounds client = await PrepareAsync(window, expectedSize, center, cancellationToken).ConfigureAwait(false);
        await RobloxKeyboardInput.TapKeyAsync(cancelPlacementVirtualKey, cancellationToken).ConfigureAwait(false);
        KeyboardInputDescriptor quick = KeyboardInputDescriptor.FromAutomationVirtualKey(quickPlacementVirtualKey);
        RobloxKeyboardInput.SendKey(quick, keyUp: false);
        try
        {
            int? selectedSlot = null;
            foreach (QuickPlacementPoint placement in placements)
            {
                client = await PrepareAsync(window, expectedSize, placement.Point, cancellationToken).ConfigureAwait(false);
                if (selectedSlot != placement.UnitSlot)
                {
                    await RobloxKeyboardInput.TapKeyAsync(
                        '0' + placement.UnitSlot,
                        RobloxInputProtocol.QuickPlacementUnitKeyHoldMilliseconds,
                        cancellationToken).ConfigureAwait(false);
                    selectedSlot = placement.UnitSlot;
                    await Task.Delay(
                        RobloxInputProtocol.QuickPlacementUnitSelectionDelayMilliseconds,
                        cancellationToken).ConfigureAwait(false);
                }
                await ClickBurstRetainingCursorAsync(client, placement.Point, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            RobloxKeyboardInput.SendKey(quick, keyUp: true);
            try
            {
                _ = await PrepareWindowAsync(window, expectedSize, CancellationToken.None).ConfigureAwait(false);
                await RobloxKeyboardInput.TapKeyAsync(
                    cancelPlacementVirtualKey,
                    CancellationToken.None).ConfigureAwait(false);
                await ParkCursorWithAcknowledgedMotionAsync(client, CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The held key is already released. A changed or closed client must not hide the primary failure.
            }
        }
        await VerifyClientSizeAsync(window, expectedSize, "quick placement batch", cancellationToken).ConfigureAwait(false);
    }

    public async Task AlignCameraAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        int shiftLockVirtualKey,
        CancellationToken cancellationToken = default)
    {
        if (!KeyboardKey.IsSupportedAutomationKey(shiftLockVirtualKey))
            throw new ArgumentOutOfRangeException(nameof(shiftLockVirtualKey));
        PixelPoint center = ClientCenter(expectedSize);
        await ScrollClientAsync(
            window,
            expectedSize,
            center,
            RobloxInputProtocol.CameraZoomWheelDelta,
            TimeSpan.FromMilliseconds(RobloxInputProtocol.CameraMotionMilliseconds),
            cancellationToken).ConfigureAwait(false);

        ClientBounds client = await PrepareAsync(window, expectedSize, center, cancellationToken).ConfigureAwait(false);
        MoveCursorWithRegisteredMotion(client, center);
        await Task.Delay(RobloxInputProtocol.ClickPositionSettleMilliseconds, cancellationToken).ConfigureAwait(false);
        bool shiftLockToggled = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RobloxKeyboardInput.TapKeyAsync(
                shiftLockVirtualKey,
                CancellationToken.None).ConfigureAwait(false);
            shiftLockToggled = true;
            await Task.Delay(RobloxInputProtocol.ShiftLockSettleMilliseconds, CancellationToken.None)
                .ConfigureAwait(false);
            await DragCameraAsync(
                RobloxInputProtocol.CameraPitchDelta,
                TimeSpan.FromMilliseconds(RobloxInputProtocol.CameraMotionMilliseconds),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (shiftLockToggled)
            {
                _ = await PrepareWindowAsync(window, expectedSize, CancellationToken.None).ConfigureAwait(false);
                await RobloxKeyboardInput.TapKeyAsync(
                    shiftLockVirtualKey,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        await VerifyClientSizeAsync(window, expectedSize, "camera alignment", cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClientBounds> PrepareAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        PixelPoint point,
        CancellationToken cancellationToken)
    {
        ClientBounds client = await PrepareWindowAsync(window, expectedSize, cancellationToken).ConfigureAwait(false);
        if (point.X < 0 || point.Y < 0 || point.X >= client.Width || point.Y >= client.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "The input point is outside the Roblox client.");
        }
        return client;
    }

    private async Task<ClientBounds> PrepareWindowAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        CancellationToken cancellationToken)
    {
        nint handle = windows.Revalidate(window);
        WindowsRobloxDisplayScale.EnsureOneHundredPercent(handle);
        ClientBounds client = windows.GetClientBounds(window);
        if (client.Size != expectedSize)
        {
            throw new InvalidOperationException($"Roblox is {client.Size}; input requires {expectedSize}.");
        }

        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        _ = await windows.EnsureClientVisibleAsync(window, expectedSize, cancellationToken).ConfigureAwait(false);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            _ = NativeInputMethods.BringWindowToTop(handle);
            if (NativeInputMethods.SetForegroundWindow(handle) || NativeMethods.GetForegroundWindow() == handle)
            {
                ClientBounds focused = windows.GetClientBounds(window);
                if (focused.Size != expectedSize)
                {
                    throw new InvalidOperationException(
                        $"Roblox changed to {focused.Size} while receiving focus; expected {expectedSize}.");
                }
                return focused;
            }
            if (attempt < 2) await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Windows did not give Roblox input focus.");
    }

    private static async Task ClickBurstRetainingCursorAsync(
        ClientBounds client,
        PixelPoint point,
        CancellationToken cancellationToken)
    {
        MoveCursorWithRegisteredMotion(client, point);
        await Task.Delay(
            RobloxInputProtocol.ClickPositionSettleMilliseconds,
            cancellationToken).ConfigureAwait(false);
        (int holdMilliseconds, int gapMilliseconds) = RobloxInputProtocol.RapidClickTiming(
            RobloxInputProtocol.QuickPlacementClickCount,
            RobloxInputProtocol.QuickPlacementBurstMilliseconds);
        for (int click = 0; click < RobloxInputProtocol.QuickPlacementClickCount; click++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeInputMethods.mouse_event(NativeInputMethods.MouseLeftDown, 0, 0, 0, 0);
            try
            {
                if (holdMilliseconds > 0)
                {
                    await Task.Delay(holdMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                NativeInputMethods.mouse_event(NativeInputMethods.MouseLeftUp, 0, 0, 0, 0);
            }
            if (click + 1 < RobloxInputProtocol.QuickPlacementClickCount && gapMilliseconds > 0)
            {
                await Task.Delay(gapMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendWheelOverTimeAsync(
        int wheelDelta,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        const int incrementCount = 20;
        int remaining = wheelDelta;
        Stopwatch clock = Stopwatch.StartNew();
        for (int index = 0; index < incrementCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int increment = RobloxInputProtocol.NextDistributedIncrement(remaining, incrementCount - index);
            remaining -= increment;
            SendMouse([MouseInput(NativeInputMethods.MouseWheel, data: increment)]);
            await DelayUntilAsync(
                clock,
                duration * ((index + 1d) / incrementCount),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DragCameraAsync(
        int deltaY,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        bool restoreCursor = NativeInputMethods.GetCursorPos(out NativeMethods.Point original);
        NativeInputMethods.mouse_event(NativeInputMethods.MouseRightDown, 0, 0, 0, 0);
        try
        {
            await Task.Delay(60, cancellationToken).ConfigureAwait(false);
            int remaining = deltaY;
            Stopwatch clock = Stopwatch.StartNew();
            for (int index = 0; index < RobloxInputProtocol.CameraInputIncrementCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int increment = RobloxInputProtocol.NextDistributedIncrement(
                    remaining,
                    RobloxInputProtocol.CameraInputIncrementCount - index);
                remaining -= increment;
                NativeInputMethods.mouse_event(NativeInputMethods.MouseMove, 0, increment, 0, 0);
                await DelayUntilAsync(
                    clock,
                    duration * ((index + 1d) / RobloxInputProtocol.CameraInputIncrementCount),
                    cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(60, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            NativeInputMethods.mouse_event(NativeInputMethods.MouseRightUp, 0, 0, 0, 0);
            if (restoreCursor) MoveScreenCursorWithRegisteredMotion(original.X, original.Y);
        }
    }

    private static async Task DelayUntilAsync(
        Stopwatch clock,
        TimeSpan targetElapsed,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = targetElapsed - clock.Elapsed;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private Task VerifyClientSizeAsync(
        RobloxWindow window,
        PixelSize expectedSize,
        string operation,
        CancellationToken cancellationToken) =>
        RobloxClientSizeStabilizer.EnsureExpectedAsync(
            () => windows.GetClientBounds(window).Size,
            expectedSize,
            operation,
            cancellationToken);

    private static PixelPoint ClientCenter(PixelSize size) => new(size.Width / 2, size.Height / 2);

    private static void MoveCursorWithRegisteredMotion(ClientBounds client, PixelPoint point)
    {
        (int first, int second) = RobloxInputProtocol.RegisteredMotionDeltas(point.X, client.Width);
        RobloxCursorMotion.SetAndPulse(
            checked(client.X + point.X), checked(client.Y + point.Y), first, second);
    }

    private static void MoveScreenCursorWithRegisteredMotion(int screenX, int screenY) =>
        RobloxCursorMotion.SetAndPulse(screenX, screenY, 1, -1);

    private static async Task ParkCursorWithAcknowledgedMotionAsync(
        ClientBounds client,
        CancellationToken cancellationToken)
    {
        PixelPoint parking = RobloxInputProtocol.ParkingPoint(client.Size);
        int parkingX = client.X + parking.X;
        int parkingY = client.Y + parking.Y;
        RobloxCursorMotion.PositionWithRetry(
            parkingX, parkingY, "Windows could not park the pointer in Roblox.");

        for (int pulse = 0; pulse < RobloxInputProtocol.HoverClearPulseCount; pulse++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int delta = pulse % 2 == 0 ? -1 : 1;
            NativeInputMethods.mouse_event(NativeInputMethods.MouseMove, delta, 0, 0, 0);
            if (pulse + 1 < RobloxInputProtocol.HoverClearPulseCount)
            {
                await Task.Delay(
                    RobloxInputProtocol.HoverClearPulseIntervalMilliseconds,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static NativeInputMethods.Input MouseInput(uint flags, int data = 0) => new()
    {
        Type = NativeInputMethods.InputMouse,
        Mouse = new NativeInputMethods.MouseInput
        {
            MouseData = unchecked((uint)data),
            Flags = flags,
        },
    };

    private static void SendMouse(NativeInputMethods.Input[] inputs)
    {
        uint sent = NativeInputMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<NativeInputMethods.Input>());
        if (sent != checked((uint)inputs.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not accept the Roblox mouse input.");
        }
    }
}
