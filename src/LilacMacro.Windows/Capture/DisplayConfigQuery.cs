using System.Runtime.InteropServices;

namespace LilacMacro.Windows.Capture;

internal static class DisplayConfigQuery
{
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const int GetSourceName = 1;
    private const int GetSdrWhiteLevel = 11;
    private const int ErrorInsufficientBuffer = 122;

    internal static int[] InteropLayoutSizes =>
    [
        Marshal.SizeOf<DisplayConfigDeviceInfoHeader>(),
        Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
        Marshal.SizeOf<DisplayConfigSdrWhiteLevel>(),
        Marshal.SizeOf<DisplayConfigPathInfo>(),
        Marshal.SizeOf<DisplayConfigModeInfo>(),
    ];

    internal static bool HasExpectedInteropLayout => InteropLayoutSizes.SequenceEqual([20, 84, 24, 72, 64]);

    public static float? TryGetSdrWhiteLevelNits(string displayName)
    {
        if (!HasExpectedInteropLayout) return null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out uint pathCount, out uint modeCount);
            if (result != 0) return null;

            DisplayConfigPathInfo[] paths = new DisplayConfigPathInfo[pathCount];
            DisplayConfigModeInfo[] modes = new DisplayConfigModeInfo[modeCount];
            result = QueryDisplayConfig(
                QueryOnlyActivePaths,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                nint.Zero);
            if (result == ErrorInsufficientBuffer) continue;
            if (result != 0) return null;

            for (int index = 0; index < pathCount; index++)
            {
                DisplayConfigPathInfo path = paths[index];
                DisplayConfigSourceDeviceName source = DisplayConfigSourceDeviceName.Create(path.SourceInfo);
                if (GetDisplayConfigSourceName(ref source) != 0 ||
                    !string.Equals(source.ViewGdiDeviceName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DisplayConfigSdrWhiteLevel white = DisplayConfigSdrWhiteLevel.Create(path.TargetInfo);
                if (GetDisplayConfigSdrWhiteLevel(ref white) != 0 || white.SdrWhiteLevel == 0) return null;
                return white.SdrWhiteLevel / 1000f * CaptureColorContext.SceneReferenceWhiteNits;
            }
            return null;
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;

        public static DisplayConfigSourceDeviceName Create(DisplayConfigPathSourceInfo source) => new()
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = source.AdapterId,
                Id = source.Id,
            },
            ViewGdiDeviceName = string.Empty,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSdrWhiteLevel
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint SdrWhiteLevel;

        public static DisplayConfigSdrWhiteLevel Create(DisplayConfigPathTargetInfo target) => new()
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetSdrWhiteLevel,
                Size = (uint)Marshal.SizeOf<DisplayConfigSdrWhiteLevel>(),
                AdapterId = target.AdapterId,
                Id = target.Id,
            },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DisplayConfigModeInfo
    {
        [FieldOffset(0)] public int InfoType;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid AdapterId;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetDisplayConfigSourceName(ref DisplayConfigSourceDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetDisplayConfigSdrWhiteLevel(ref DisplayConfigSdrWhiteLevel request);
}
