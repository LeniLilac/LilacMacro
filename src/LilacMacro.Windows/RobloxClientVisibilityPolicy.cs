namespace LilacMacro.Windows;

internal static class RobloxClientVisibilityPolicy
{
    public static WindowBounds FitWindow(
        ClientBounds client,
        WindowBounds window,
        ScreenWorkArea workArea)
    {
        if (client.Width <= 0 || client.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(client));
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        if (client.Width > workArea.Width || client.Height > workArea.Height)
        {
            throw new InvalidOperationException(
                $"The Roblox client {client.Width}x{client.Height} does not fit in the usable " +
                $"monitor area {workArea.Width}x{workArea.Height}.");
        }

        int clientX = Math.Clamp(client.X, workArea.Left, workArea.Right - client.Width);
        int clientY = Math.Clamp(client.Y, workArea.Top, workArea.Bottom - client.Height);
        return window with
        {
            X = checked(window.X + clientX - client.X),
            Y = checked(window.Y + clientY - client.Y),
        };
    }

    public static bool IsFullyVisible(ClientBounds client, ScreenWorkArea workArea) =>
        client.X >= workArea.Left &&
        client.Y >= workArea.Top &&
        checked(client.X + client.Width) <= workArea.Right &&
        checked(client.Y + client.Height) <= workArea.Bottom;
}

internal readonly record struct ScreenWorkArea(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
