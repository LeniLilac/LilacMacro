namespace LilacMacro.Core.Runtime;

public sealed class RobloxDisplayScaleException(int scalePercentage)
    : InvalidOperationException(
        $"Roblox is on a monitor using {scalePercentage}% Windows display scale. " +
        "Change that monitor to 100%, then restart Roblox and retry.")
{
    public int ScalePercentage { get; } = scalePercentage;
}
