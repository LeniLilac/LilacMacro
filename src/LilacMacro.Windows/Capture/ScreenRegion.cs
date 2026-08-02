namespace LilacMacro.Windows.Capture;

internal readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);
}
