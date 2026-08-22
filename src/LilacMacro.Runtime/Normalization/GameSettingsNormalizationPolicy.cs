using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Runtime.Normalization;

internal enum GameSettingsToggleState
{
    Unknown,
    Off,
    On,
}

internal sealed record GameSettingsTabPlan(
    string Name,
    PixelPoint TabPoint,
    IReadOnlyList<GameSettingsToggleTarget> InitialTargets,
    int ScrollDelta = 0,
    IReadOnlyList<GameSettingsToggleTarget>? ScrolledTargets = null);

internal sealed record GameSettingsToggleTarget(string Name, PixelPoint Point, bool DesiredOn);

internal static class GameSettingsNormalizationPolicy
{
    public static readonly PixelPoint ScrollAnchor = new(683, 350);
    public static readonly TimeSpan ScrollDuration = TimeSpan.FromSeconds(5);

    public static IReadOnlyList<GameSettingsTabPlan> Tabs { get; } =
    [
        new("Gameplay", new PixelPoint(326, 247),
        [
            Target("Auto Skip Waves", 1069, 212, true),
            Target("Auto Vote Start", 735, 270, false),
            Target("Show Match End Rewards", 1069, 270, false),
            Target("Display Pinned Quests", 735, 329, false),
            Target("Select Unit on Placement", 1069, 329, false),
            Target("Display Path Visualizers", 1069, 387, false),
            Target("Auto Retry", 735, 445, false),
            Target("Auto Next", 1069, 445, false),
        ]),
        new("Graphics", new PixelPoint(326, 286),
        [
            Target("Show Camera Shake", 735, 212, false),
            Target("Show Depth of Field", 1069, 212, false),
            Target("Low Detail Mode", 735, 270, true),
            Target("Night Time Enabled", 1069, 270, false),
            Target("Event Theme Enabled", 735, 329, false),
        ]),
        new("Units", new PixelPoint(326, 325),
        [
            Target("Show Others' Units", 735, 212, false),
            Target("Show Others' Unit VFX", 1069, 212, false),
            Target("Show Own Unit VFX", 735, 270, false),
            Target("Show Ability Effects", 1069, 270, false),
            Target("Show Unit Aura VFX", 735, 329, false),
            Target("Show Trait Aura VFX", 1069, 329, false),
            Target("Show Buff Indicators", 735, 387, false),
            Target("Show Damage Indicators", 1069, 387, false),
            Target("Display Placement Hitboxes", 735, 445, false),
            Target("Display Unit Circles", 1069, 445, false),
            Target("Auto-Place Phantoms", 735, 503, true),
            Target("Strict Auto-Upgrade Priority", 1069, 503, true),
            Target("Strict Phantom Placement", 735, 561, true),
            Target("Prioritize Phantom Placement", 1069, 561, true),
        ],
        ScrollDelta: -5000,
        ScrolledTargets:
        [
            Target("Auto-Upgrade Placed Units", 735, 423, false),
            Target("Auto Abilities on Placement", 1069, 423, true),
            Target("Lock Farms on Placement", 735, 482, false),
        ]),
        new("Enemies", new PixelPoint(326, 364),
        [
            Target("Display Health Bars", 735, 212, false),
            Target("Show Enemy Modifiers", 1069, 212, false),
            Target("Show Enemy Status Effects", 735, 270, false),
            Target("Show Enemy Effects", 1069, 270, false),
        ]),
        new("Miscellaneous", new PixelPoint(326, 403),
        [
            Target("Display Update Log on Login", 735, 445, false),
            Target("Auto Sprint", 1069, 445, true),
        ]),
    ];

    public static GameSettingsToggleState Classify(RgbImage image, PixelPoint point)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Size != new PixelSize(1366, 700))
            throw new InvalidDataException("Game settings vision requires a 1366 by 700 RGB client image.");

        int red = 0;
        int green = 0;
        int samples = 0;
        for (int y = point.Y - 18; y <= point.Y + 18; y++)
        {
            for (int x = point.X - 18; x <= point.X + 18; x++)
            {
                int pixel = checked((y * image.Size.Width + x) * 3);
                byte r = image.Pixels[pixel];
                byte g = image.Pixels[pixel + 1];
                byte b = image.Pixels[pixel + 2];
                samples++;
                if (g >= 75 && g >= r * 1.3 && g >= b * 1.1) green++;
                if (r >= 85 && r >= g * 1.3 && r >= b * 1.15) red++;
            }
        }

        double greenFraction = green / (double)samples;
        double redFraction = red / (double)samples;
        if (greenFraction >= 0.55 && redFraction <= 0.08) return GameSettingsToggleState.On;
        if (redFraction >= 0.50 && greenFraction <= 0.08) return GameSettingsToggleState.Off;
        return GameSettingsToggleState.Unknown;
    }

    private static GameSettingsToggleTarget Target(string name, int x, int y, bool desiredOn) =>
        new(name, new PixelPoint(x, y), desiredOn);
}
